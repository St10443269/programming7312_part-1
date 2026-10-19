using System.Text.RegularExpressions;
using SmartX.Api.Services;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>A validated telemetry packet waiting in one of the queues.</summary>
public sealed class TelemetryMessage
{
    public required long ArrivalOrder { get; init; }
    public required string MacAddress { get; init; }
    public required string DeviceName { get; init; }
    public required string SensorType { get; init; }
    public required double Value { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required ValidationSeverity Severity { get; init; }
    public string? AlertCode { get; init; }
    public string? AlertText { get; init; }
}

/// <summary>Result of offering a packet to the processor. <see cref="StatusCode"/> is the HTTP status to return.</summary>
public sealed record EnqueueResult(bool Accepted, int StatusCode, string Message, TelemetryMessage? Queued = null);

/// <summary>
/// Validates incoming telemetry, queues it, and processes it in priority
/// order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Normal messages</b> (Info severity) go into a <see cref="Queue{T}"/>
/// and are processed First-In-First-Out, i.e. in the order they arrived.
/// </para>
/// <para>
/// <b>Alerts</b> (Warning/Critical) go into a <see cref="PriorityQueue{TElement,TPriority}"/>.
/// The priority is the tuple (level, arrival order): level 0 = Critical,
/// 1 = Warning. A PriorityQueue always dequeues the smallest priority
/// first, so Critical beats Warning, and within the same level the earlier
/// arrival wins (the arrival number breaks the tie, keeping it fair).
/// </para>
/// <para>
/// <b>Processing order:</b> the priority queue is served before the normal
/// queue. To stop a flood of alerts starving normal telemetry, after
/// <see cref="RealTimeOptions.ProcessingOptions.MaxConsecutivePriority"/>
/// alerts in a row one normal message is taken (if any are waiting) before
/// going back to alerts.
/// </para>
/// </remarks>
public sealed partial class TelemetryProcessor(
    DeviceRegistry registry,
    HistoryLog history,
    ActiveErrorTracker errors,
    TelemetryClassifier classifier,
    LiveUpdateBroadcaster broadcaster,
    ITelemetryStore partOneStore,
    RealTimeOptions options,
    TimeProvider clock)
{
    public const string PriorityQueueSource = "PriorityQueue";
    public const string NormalQueueSource = "Queue";
    private const int RecentLogSize = 40;

    private readonly Queue<TelemetryMessage> _normalQueue = new();
    private readonly PriorityQueue<TelemetryMessage, (int Level, long Arrival)> _priorityQueue = new();
    private readonly object _queueSync = new();
    private long _arrivalCounter;
    private int _consecutivePriority;
    private volatile bool _isPaused;

    private readonly Queue<ProcessedMessageDto> _recentlyProcessed = new();
    private readonly object _statsSync = new();
    private long _processedCounter;
    private long _rejectedCounter;

    [GeneratedRegex("^([0-9A-F]{2}:){5}[0-9A-F]{2}$")]
    private static partial Regex MacPattern();

    public bool IsPaused => _isPaused;

    /// <summary>Pausing stops the worker draining the queues, so you can watch them fill and then see the processing order on resume.</summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        PublishSnapshot();
    }

    // ---------------------------------------------------------------- intake

    /// <summary>Validates a packet and, if it is good, puts it in the right queue.</summary>
    public EnqueueResult Enqueue(TelemetryPacketRequest packet)
    {
        var result = Validate(packet);
        if (!result.Accepted)
        {
            Interlocked.Increment(ref _rejectedCounter);
            return result;
        }

        lock (_queueSync)
        {
            AddToQueue(result, out var queued);
            return result with { Queued = queued };
        }
    }

    /// <summary>
    /// Queues several packets atomically (the worker cannot run in between),
    /// used by the priority demo so the processing order is deterministic.
    /// </summary>
    public List<EnqueueResult> EnqueueMany(IEnumerable<TelemetryPacketRequest> packets)
    {
        var validated = packets.Select(Validate).ToList();
        var results = new List<EnqueueResult>();

        lock (_queueSync)
        {
            foreach (var result in validated)
            {
                if (!result.Accepted)
                {
                    Interlocked.Increment(ref _rejectedCounter);
                    results.Add(result);
                    continue;
                }

                AddToQueue(result, out var queued);
                results.Add(result with { Queued = queued });
            }
        }

        return results;
    }

    private void AddToQueue(EnqueueResult validated, out TelemetryMessage queued)
    {
        // Caller holds _queueSync. The arrival number is assigned here, under
        // the lock, so it matches the true order messages entered the queues.
        var draft = validated.Queued!;
        queued = new TelemetryMessage
        {
            ArrivalOrder = ++_arrivalCounter,
            MacAddress = draft.MacAddress,
            DeviceName = draft.DeviceName,
            SensorType = draft.SensorType,
            Value = draft.Value,
            CapturedAtUtc = draft.CapturedAtUtc,
            ReceivedAtUtc = draft.ReceivedAtUtc,
            Severity = draft.Severity,
            AlertCode = draft.AlertCode,
            AlertText = draft.AlertText
        };

        if (queued.Severity == ValidationSeverity.Info)
        {
            _normalQueue.Enqueue(queued);
        }
        else
        {
            _priorityQueue.Enqueue(queued, (TelemetryClassifier.PriorityFor(queued.Severity), queued.ArrivalOrder));
        }
    }

    private EnqueueResult Validate(TelemetryPacketRequest packet)
    {
        if (string.IsNullOrWhiteSpace(packet.MacAddress))
            return new(false, 400, "MacAddress is required.");

        var mac = DeviceRegistry.NormaliseMac(packet.MacAddress);
        if (!MacPattern().IsMatch(mac))
            return new(false, 400, $"'{packet.MacAddress}' is not a valid MAC address (expected e.g. 24:6F:28:B1:00:01).");

        if (string.IsNullOrWhiteSpace(packet.SensorType))
            return new(false, 400, "SensorType is required.");

        if (packet.Value is not { } value || double.IsNaN(value) || double.IsInfinity(value))
            return new(false, 400, "Value is required and must be a finite number.");

        // Dictionary lookup (O(1)) - an unknown device is rejected cleanly
        // rather than crashing anything further down the pipeline.
        if (!registry.TryGet(mac, out var device))
            return new(false, 404, $"Unknown device '{mac}'. Register it before sending telemetry.");

        if (!string.Equals(packet.SensorType, device.SensorType, StringComparison.Ordinal))
            return new(false, 400, $"{device.Name} reports '{device.SensorType}', not '{packet.SensorType}'.");

        if (TelemetryClassifier.ValidateRange(device.SensorType, value) is { } rangeError)
            return new(false, 400, rangeError);

        var now = clock.GetUtcNow();
        var capturedAt = packet.CapturedAtUtc ?? now;
        if (capturedAt > now.AddMinutes(5))
            return new(false, 400, "CapturedAtUtc is in the future.");

        var classification = classifier.Classify(device.SensorType, value);
        var draft = new TelemetryMessage
        {
            ArrivalOrder = 0,
            MacAddress = mac,
            DeviceName = device.Name,
            SensorType = device.SensorType,
            Value = value,
            CapturedAtUtc = capturedAt,
            ReceivedAtUtc = now,
            Severity = classification.Severity,
            AlertCode = classification.AlertCode,
            AlertText = classification.AlertText
        };

        var queueName = classification.Severity == ValidationSeverity.Info
            ? "normal queue"
            : $"priority queue (priority {TelemetryClassifier.PriorityFor(classification.Severity)})";
        return new(true, 202, $"Accepted into the {queueName}.", draft);
    }

    // ------------------------------------------------------------ processing

    /// <summary>
    /// Takes the next message: the priority queue first, except that after
    /// MaxConsecutivePriority alerts in a row one waiting normal message is
    /// let through (the starvation guard).
    /// </summary>
    public bool TryDequeueNext(out TelemetryMessage message, out string source)
    {
        lock (_queueSync)
        {
            var normalWaiting = _normalQueue.Count > 0;
            var priorityWaiting = _priorityQueue.Count > 0;
            var guardTriggered = normalWaiting && _consecutivePriority >= options.Processing.MaxConsecutivePriority;

            if (priorityWaiting && !guardTriggered)
            {
                message = _priorityQueue.Dequeue();
                source = PriorityQueueSource;
                _consecutivePriority++;
                return true;
            }

            if (normalWaiting)
            {
                message = _normalQueue.Dequeue();
                source = NormalQueueSource;
                _consecutivePriority = 0;
                return true;
            }

            message = null!;
            source = "";
            return false;
        }
    }

    /// <summary>Processes up to <paramref name="max"/> queued messages. Returns how many were processed.</summary>
    public int ProcessPending(int max)
    {
        var count = 0;
        while (count < max && TryDequeueNext(out var message, out var source))
        {
            Process(message, source);
            count++;
        }

        return count;
    }

    private void Process(TelemetryMessage message, string source)
    {
        if (!registry.TryGet(message.MacAddress, out var device))
        {
            Interlocked.Increment(ref _rejectedCounter);
            return;
        }

        var errorsChanged = false;
        var wasConnected = device.IsConnected;
        device.ApplyReading(message.Value, message.ReceivedAtUtc);

        // Any reading proves the device is back online.
        if (!wasConnected && errors.Resolve(device.MacAddress, TelemetryClassifier.Disconnected))
        {
            Publish(history.Add(message.ReceivedAtUtc, HistoryEntryKind.Recovery, device.MacAddress, device.Name,
                $"{device.Name} reconnected", code: TelemetryClassifier.Disconnected));
            errorsChanged = true;
        }

        var readingEntry = history.Add(message.CapturedAtUtc, HistoryEntryKind.Reading, device.MacAddress, device.Name,
            $"{message.SensorType} = {message.Value:0.##} {device.Unit}".TrimEnd(),
            message.Severity, message.SensorType, message.Value);

        if (message.AlertCode is { } code)
        {
            // HashSet de-duplication: only the first report raises an alert.
            if (errors.Report(device.MacAddress, device.Name, code, message.AlertText ?? code, message.ReceivedAtUtc))
            {
                Publish(history.Add(message.CapturedAtUtc, HistoryEntryKind.Alert, device.MacAddress, device.Name,
                    message.AlertText ?? code, ValidationSeverity.Critical, message.SensorType, message.Value, code));
            }

            errorsChanged = true;
        }
        else if (message.Severity == ValidationSeverity.Info
                 && TelemetryClassifier.AlertCodeFor(message.SensorType) is { } watchedCode
                 && errors.Resolve(device.MacAddress, watchedCode))
        {
            // Cleared only once fully back to normal (Info), not merely below
            // Critical - stops an alert flapping on and off around the threshold.
            Publish(history.Add(message.CapturedAtUtc, HistoryEntryKind.Recovery, device.MacAddress, device.Name,
                $"{device.Name} back to normal ({message.Value:0.##} {device.Unit})".TrimEnd(), code: watchedCode));
            errorsChanged = true;
        }

        ForwardToPartOne(device, message.Value);
        RecordProcessed(message, source);

        Publish(readingEntry);
        broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.DeviceType, Device = device.ToDto(errors) });
        if (errorsChanged)
        {
            PublishErrors();
        }
    }

    /// <summary>Marks devices that have gone silent as Disconnected and raises a (de-duplicated) DISCONNECTED error.</summary>
    public void SweepDisconnected()
    {
        var now = clock.GetUtcNow();
        var cutoff = now.AddSeconds(-options.Thresholds.DisconnectAfterSeconds);
        var anyChanged = false;

        foreach (var device in registry.GetAll())
        {
            if (!device.MarkDisconnectedIfSilentSince(cutoff, registry.GetRegisteredAt(device.MacAddress)))
            {
                continue;
            }

            anyChanged = true;
            var message = $"{device.Name} has sent no telemetry for over {options.Thresholds.DisconnectAfterSeconds}s";
            if (errors.Report(device.MacAddress, device.Name, TelemetryClassifier.Disconnected, message, now))
            {
                Publish(history.Add(now, HistoryEntryKind.Alert, device.MacAddress, device.Name, message,
                    ValidationSeverity.Critical, code: TelemetryClassifier.Disconnected));
            }

            broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.DeviceType, Device = device.ToDto(errors) });
        }

        if (anyChanged)
        {
            PublishErrors();
        }
    }

    /// <summary>
    /// Part 1 integration: processed readings also feed the Part 1 pulse
    /// feed and grid heatmap, so both modules show the same live data.
    /// </summary>
    private void ForwardToPartOne(SimulatedDevice device, double value)
    {
        switch (device.SensorType)
        {
            case "PowerWatts":
                partOneStore.IngestInt(device.Id, device.Name, device.SensorType, (int)Math.Round(value),
                    partOneStore.ResolveZoneIndex(device.ZoneName));
                break;
            case "RelayClosed":
                partOneStore.IngestBool(device.Id, device.Name, device.SensorType, value >= 0.5);
                break;
            default:
                partOneStore.IngestFloat(device.Id, device.Name, device.SensorType, (float)value);
                break;
        }
    }

    // --------------------------------------------------------------- reporting

    private void RecordProcessed(TelemetryMessage message, string source)
    {
        lock (_statsSync)
        {
            _recentlyProcessed.Enqueue(new ProcessedMessageDto
            {
                ProcessedOrder = ++_processedCounter,
                ArrivalOrder = message.ArrivalOrder,
                MacAddress = message.MacAddress,
                DeviceName = message.DeviceName,
                SensorType = message.SensorType,
                Value = message.Value,
                Severity = message.Severity,
                Source = source,
                ProcessedAtUtc = clock.GetUtcNow()
            });

            while (_recentlyProcessed.Count > RecentLogSize)
            {
                _recentlyProcessed.Dequeue();
            }
        }
    }

    public ProcessingSnapshotDto GetSnapshot()
    {
        int normalDepth, priorityDepth;
        lock (_queueSync)
        {
            normalDepth = _normalQueue.Count;
            priorityDepth = _priorityQueue.Count;
        }

        lock (_statsSync)
        {
            return new ProcessingSnapshotDto
            {
                NormalQueueDepth = normalDepth,
                PriorityQueueDepth = priorityDepth,
                IsPaused = _isPaused,
                TotalProcessed = _processedCounter,
                TotalRejected = Interlocked.Read(ref _rejectedCounter),
                MaxConsecutivePriority = options.Processing.MaxConsecutivePriority,
                RecentlyProcessed = _recentlyProcessed.Reverse().ToList()
            };
        }
    }

    public void PublishSnapshot() =>
        broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.ProcessingType, Processing = GetSnapshot() });

    private void Publish(HistoryEntryDto entry) =>
        broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.HistoryType, Entry = entry });

    private void PublishErrors() =>
        broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.ErrorsType, Errors = errors.GetAll() });
}
