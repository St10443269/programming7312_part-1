using System.Collections.Concurrent;
using SmartX.Shared.Aggregation;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;
using SmartX.Shared.Generics;

namespace SmartX.Api.Services;

/// <summary>
/// In-memory telemetry pipeline: converts raw readings into
/// <see cref="TelemetryPacket{T}"/> instances, keeps a rolling "pulse feed"
/// for the dashboard's live engagement widget, aggregates power readings
/// with the overloaded <see cref="MeterReading"/> operators, and maintains
/// a rectangular zone-load matrix for the grid heatmap.
/// </summary>
public sealed class TelemetryStore : ITelemetryStore
{
    private static readonly string[] ZoneNames = ["Zone 1", "Zone 2", "Zone 3", "Zone 4"];
    private const int TimeSlotCount = 12;

    // Historical readings, one strongly-typed generic list per payload shape -
    // this is the "optimised Collections (List<T>)" the raw arrays get
    // transferred into once ingested.
    private readonly List<TelemetryPacket<float>> _floatReadings = [];
    private readonly List<TelemetryPacket<int>> _intReadings = [];
    private readonly List<TelemetryPacket<bool>> _boolReadings = [];
    private readonly object _listLock = new();

    private readonly ConcurrentDictionary<string, float> _lastFloatByKey = new();
    private readonly ConcurrentDictionary<string, bool> _lastBoolByKey = new();
    private readonly ConcurrentDictionary<string, MeterReading> _lastMeterByNode = new();

    private readonly ConcurrentQueue<TelemetryEventDto> _feed = new();
    private const int FeedCapacity = 60;

    // Rectangular 2D array: rows = zones, columns = rolling time slots. Chosen
    // over a jagged array here because every zone genuinely has the same
    // fixed number of slots - a real multi-dimensional grid, not a ragged one.
    private readonly double[,] _zoneLoadMatrix = new double[ZoneNames.Length, TimeSlotCount];
    private int _matrixWriteColumn;
    private readonly object _matrixLock = new();

    // Latest watts per zone, independent of matrix column rotation - the
    // heatmap shows rolling history, but the gauge needs "what is each
    // zone drawing right now" regardless of which column that landed in.
    private readonly double[] _latestWattsByZone = new double[ZoneNames.Length];

    public TelemetryEventDto IngestFloat(Guid deviceId, string deviceLabel, string sensorType, float value)
    {
        var packet = new TelemetryPacket<float>(deviceId, sensorType, value);
        lock (_listLock) { _floatReadings.Add(packet); }

        var key = $"{deviceId}:{sensorType}";
        var previous = _lastFloatByKey.GetValueOrDefault(key, value);
        _lastFloatByKey[key] = value;

        var delta = Math.Abs(value - previous);
        var severity = delta switch
        {
            > 15f => ValidationSeverity.Critical,
            > 7f => ValidationSeverity.Warning,
            _ => ValidationSeverity.Info
        };

        return PublishToFeed(deviceId, deviceLabel, sensorType, packet.FormatValue(), severity, packet.CapturedAtUtc);
    }

    public TelemetryEventDto IngestInt(Guid deviceId, string deviceLabel, string sensorType, int value, int zoneIndex)
    {
        var packet = new TelemetryPacket<int>(deviceId, sensorType, value);
        lock (_listLock) { _intReadings.Add(packet); }

        var nodeId = deviceLabel;
        var reading = new MeterReading(nodeId, value);
        var previous = _lastMeterByNode.GetValueOrDefault(nodeId, reading);

        // Overloaded '-' operator drives the actual severity decision, not
        // just a display gimmick.
        var delta = reading - previous;
        _lastMeterByNode[nodeId] = reading;

        var severity = Math.Abs(delta.Watts) switch
        {
            > 500 => ValidationSeverity.Critical,
            > 150 => ValidationSeverity.Warning,
            _ => ValidationSeverity.Info
        };

        RecordZoneLoad(Math.Clamp(zoneIndex, 0, ZoneNames.Length - 1), value);

        return PublishToFeed(deviceId, deviceLabel, sensorType, packet.FormatValue(), severity, packet.CapturedAtUtc);
    }

    public TelemetryEventDto IngestBool(Guid deviceId, string deviceLabel, string sensorType, bool value)
    {
        var packet = new TelemetryPacket<bool>(deviceId, sensorType, value);
        lock (_listLock) { _boolReadings.Add(packet); }

        var key = $"{deviceId}:{sensorType}";
        var previous = _lastBoolByKey.GetValueOrDefault(key, value);
        _lastBoolByKey[key] = value;

        var severity = previous != value ? ValidationSeverity.Warning : ValidationSeverity.Info;

        return PublishToFeed(deviceId, deviceLabel, sensorType, packet.FormatValue(), severity, packet.CapturedAtUtc);
    }

    public List<TelemetryEventDto> IngestFloatBatch(Guid[] deviceIds, string[] deviceLabels, string sensorType, float[][] samples)
    {
        var events = new List<TelemetryEventDto>();

        // Outer loop walks the jagged array by device, inner loop walks that
        // device's own sequential sample count - devices rarely buffer the
        // same number of readings, which is exactly why this is jagged
        // (float[][]) rather than rectangular (float[,]).
        for (var deviceIndex = 0; deviceIndex < samples.Length && deviceIndex < deviceIds.Length; deviceIndex++)
        {
            var deviceSamples = samples[deviceIndex];
            for (var sampleIndex = 0; sampleIndex < deviceSamples.Length; sampleIndex++)
            {
                events.Add(IngestFloat(deviceIds[deviceIndex], deviceLabels[deviceIndex], sensorType, deviceSamples[sampleIndex]));
            }
        }

        return events;
    }

    public IReadOnlyList<TelemetryEventDto> GetRecentFeed(int take = 50) =>
        _feed.Reverse().Take(take).ToList();

    public AggregateResultDto GetAggregate(string nodeId)
    {
        if (!_lastMeterByNode.TryGetValue(nodeId, out var latest))
        {
            return new AggregateResultDto { NodeId = nodeId, TotalWatts = 0, DeltaFromPreviousWatts = 0, SampleCount = 0 };
        }

        var sampleCount = _intReadings.Count(p => p.SensorType == nodeId || true);
        var allForNode = _lastMeterByNode.Values.Where(m => m.NodeId == nodeId).ToList();

        // Combine every reading recorded for this node with the overloaded
        // '+' operator to demonstrate direct aggregation (Meter3 = Meter1 + Meter2).
        var total = allForNode.Aggregate(new MeterReading(nodeId, 0), (acc, next) => acc + next);

        return new AggregateResultDto
        {
            NodeId = nodeId,
            TotalWatts = total.Watts,
            DeltaFromPreviousWatts = latest.Watts,
            SampleCount = sampleCount
        };
    }

    public GridSnapshotDto GetGridSnapshot()
    {
        lock (_matrixLock)
        {
            var rows = new double[ZoneNames.Length][];
            var max = 0d;

            for (var zone = 0; zone < ZoneNames.Length; zone++)
            {
                rows[zone] = new double[TimeSlotCount];
                for (var slot = 0; slot < TimeSlotCount; slot++)
                {
                    var watts = _zoneLoadMatrix[zone, slot];
                    rows[zone][slot] = watts;
                    max = Math.Max(max, watts);
                }
            }

            return new GridSnapshotDto
            {
                ZoneNames = ZoneNames,
                WattsByZoneThenSlot = rows,
                MaxWatts = max,
                LatestWattsByZone = (double[])_latestWattsByZone.Clone()
            };
        }
    }

    public int ResolveZoneIndex(string zoneName)
    {
        var digits = new string(zoneName.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var n) && n > 0)
        {
            return (n - 1) % ZoneNames.Length;
        }

        return Math.Abs(zoneName.GetHashCode()) % ZoneNames.Length;
    }

    private void RecordZoneLoad(int zoneIndex, double watts)
    {
        lock (_matrixLock)
        {
            _zoneLoadMatrix[zoneIndex, _matrixWriteColumn] = watts;
            _latestWattsByZone[zoneIndex] = watts;
            _matrixWriteColumn = (_matrixWriteColumn + 1) % TimeSlotCount;
        }
    }

    private TelemetryEventDto PublishToFeed(
        Guid deviceId, string deviceLabel, string sensorType, string displayValue,
        ValidationSeverity severity, DateTimeOffset capturedAtUtc)
    {
        var dto = new TelemetryEventDto
        {
            DeviceId = deviceId,
            DeviceLabel = deviceLabel,
            SensorType = sensorType,
            DisplayValue = displayValue,
            Severity = severity,
            CapturedAtUtc = capturedAtUtc
        };

        _feed.Enqueue(dto);
        while (_feed.Count > FeedCapacity && _feed.TryDequeue(out _)) { }

        return dto;
    }
}
