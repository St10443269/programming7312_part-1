using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SmartX.Api.RealTime;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.Endpoints;

/// <summary>Part 2 telemetry intake, queue inspection and the Server-Sent Events live stream.</summary>
public static class StreamEndpoints
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static RouteGroupBuilder MapStreamEndpoints(this RouteGroupBuilder group)
    {
        // One telemetry packet from a (simulated) device. Validated and queued;
        // the background worker processes it shortly after.
        group.MapPost("/telemetry", async (HttpRequest request, TelemetryProcessor processor) =>
        {
            var (packet, error) = await RequestBody.TryReadAsync<TelemetryPacketRequest>(request);
            if (packet is null)
            {
                return Results.BadRequest(new { message = error });
            }

            var result = processor.Enqueue(packet);
            return result.Accepted
                ? Results.Accepted(value: new { message = result.Message, arrivalOrder = result.Queued!.ArrivalOrder, severity = result.Queued.Severity })
                : Results.Json(new { message = result.Message }, statusCode: result.StatusCode);
        });

        group.MapGet("/processing", (TelemetryProcessor processor) => Results.Ok(processor.GetSnapshot()));

        group.MapPost("/processing/pause", (TelemetryProcessor processor) =>
        {
            processor.SetPaused(true);
            return Results.Ok(processor.GetSnapshot());
        });

        group.MapPost("/processing/resume", (TelemetryProcessor processor) =>
        {
            processor.SetPaused(false);
            return Results.Ok(processor.GetSnapshot());
        });

        // Priority demo: queues 8 normal readings, then 1 warning, then 1
        // critical power spike - all in one atomic step. The processing log
        // then shows the critical handled first, the warning second, and the
        // normal readings after, in their original FIFO order.
        group.MapPost("/demo/priority-burst", (TelemetryProcessor processor, DeviceRegistry registry) =>
        {
            var packets = BuildPriorityBurst(registry);
            if (packets.Count == 0)
            {
                return Results.Conflict(new { message = "The demo network devices are not registered." });
            }

            var results = processor.EnqueueMany(packets);
            processor.PublishSnapshot();
            return Results.Ok(results.Select(r => new
            {
                r.Accepted,
                r.Message,
                arrivalOrder = r.Queued?.ArrivalOrder,
                device = r.Queued?.DeviceName,
                severity = r.Queued?.Severity
            }));
        });

        // Server-Sent Events: the browser keeps this request open and the API
        // writes a "data:" line each time something changes.
        group.MapGet("/live", (LiveUpdateBroadcaster broadcaster, DeviceRegistry registry, ActiveErrorTracker errors,
                TelemetryProcessor processor, CancellationToken requestAborted) =>
            TypedResults.ServerSentEvents(StreamUpdates(broadcaster, registry, errors, processor, requestAborted)));

        return group;
    }

    private static async IAsyncEnumerable<SseItem<LiveUpdateDto>> StreamUpdates(
        LiveUpdateBroadcaster broadcaster, DeviceRegistry registry, ActiveErrorTracker errors, TelemetryProcessor processor,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        var (subscriberId, reader) = broadcaster.Subscribe();
        try
        {
            // Start every (re)connection with a full snapshot, so a dashboard
            // that was disconnected for a while catches up immediately.
            foreach (var device in registry.GetAll())
            {
                yield return Item(new LiveUpdateDto { Type = LiveUpdateDto.DeviceType, Device = device.ToDto(errors) });
            }

            yield return Item(new LiveUpdateDto { Type = LiveUpdateDto.ErrorsType, Errors = errors.GetAll() });
            yield return Item(new LiveUpdateDto { Type = LiveUpdateDto.ProcessingType, Processing = processor.GetSnapshot() });

            while (!requestAborted.IsCancellationRequested)
            {
                if (!await WaitForUpdateAsync(reader, requestAborted))
                {
                    // Nothing for a while - a heartbeat keeps the connection
                    // open and lets the client notice if it has actually died.
                    yield return Item(new LiveUpdateDto { Type = LiveUpdateDto.HeartbeatType });
                    continue;
                }

                while (reader.TryRead(out var update))
                {
                    yield return Item(update);
                }
            }
        }
        finally
        {
            // Runs when the browser disconnects (request aborted), so dead
            // connections never keep receiving updates.
            broadcaster.Unsubscribe(subscriberId);
        }
    }

    /// <summary>Waits for an update or the heartbeat interval. Returns false on heartbeat timeout or when the client has gone.</summary>
    private static async Task<bool> WaitForUpdateAsync(ChannelReader<LiveUpdateDto> reader, CancellationToken requestAborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(HeartbeatInterval);
        try
        {
            return await reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static SseItem<LiveUpdateDto> Item(LiveUpdateDto update) => new(update, update.Type);

    private static List<TelemetryPacketRequest> BuildPriorityBurst(DeviceRegistry registry)
    {
        var devices = registry.GetAll();
        var packets = new List<TelemetryPacketRequest>();

        foreach (var device in devices.Take(8))
        {
            packets.Add(new TelemetryPacketRequest
            {
                MacAddress = device.MacAddress,
                SensorType = device.SensorType,
                Value = NormalValueFor(device)
            });
        }

        var moisture = devices.FirstOrDefault(d => d.Kind == DeviceKind.MoistureSensor);
        if (moisture is not null)
        {
            packets.Add(new TelemetryPacketRequest { MacAddress = moisture.MacAddress, SensorType = moisture.SensorType, Value = 25 });
        }

        var plug = devices.FirstOrDefault(d => d.Kind == DeviceKind.SmartPlug);
        if (plug is not null)
        {
            packets.Add(new TelemetryPacketRequest { MacAddress = plug.MacAddress, SensorType = plug.SensorType, Value = 1900 });
        }

        return packets;
    }

    // A healthy (Info-level) reading that matches the device's current on/off state.
    private static double NormalValueFor(SimulatedDevice device) => device.Kind switch
    {
        DeviceKind.MoistureSensor => 45,
        DeviceKind.WaterPump => device.IsOn ? 12 : 0,
        DeviceKind.SmartPlug => device.IsOn ? 150 : 0,
        DeviceKind.Relay => device.IsOn ? 1 : 0,
        DeviceKind.PowerMeter => 400,
        _ => 25
    };
}
