using SmartX.Api.Services;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static RouteGroupBuilder MapTelemetryEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", (TelemetryIngestRequest request, ISensorRepository sensors, ITelemetryStore telemetry) =>
        {
            var sensor = sensors.GetById(request.DeviceId);
            var label = sensor?.NodeId ?? request.DeviceId.ToString("N")[..8];
            var zoneIndex = sensor is null ? 0 : telemetry.ResolveZoneIndex(sensor.ZoneName);

            var result = request.DataType switch
            {
                TelemetryDataType.Float when request.FloatValue is { } f =>
                    telemetry.IngestFloat(request.DeviceId, label, request.SensorType, f),

                TelemetryDataType.Integer when request.IntValue is { } n =>
                    telemetry.IngestInt(request.DeviceId, label, request.SensorType, n, zoneIndex),

                TelemetryDataType.Boolean when request.BoolValue is { } b =>
                    telemetry.IngestBool(request.DeviceId, label, request.SensorType, b),

                _ => null
            };

            return result is null
                ? Results.BadRequest(new { message = $"Request declared DataType={request.DataType} but the matching value field was not provided." })
                : Results.Ok(result);
        });

        group.MapPost("/batch", (BatchTelemetryIngestRequest request, ISensorRepository sensors, ITelemetryStore telemetry) =>
        {
            if (request.DeviceIds.Length != request.Samples.Length)
            {
                return Results.BadRequest(new { message = "DeviceIds and Samples must have the same length - one sample array per device." });
            }

            var labels = request.DeviceIds
                .Select(id => sensors.GetById(id)?.NodeId ?? id.ToString("N")[..8])
                .ToArray();

            var events = telemetry.IngestFloatBatch(request.DeviceIds, labels, request.SensorType, request.Samples);
            return Results.Ok(events);
        });

        group.MapPost("/seed", (SeedTelemetryRequest request, MockTelemetrySeeder seeder) =>
        {
            var count = Math.Clamp(request.Count, 1, 200);
            return Results.Ok(seeder.SeedRandomBatch(count));
        });

        group.MapGet("/feed", (ITelemetryStore telemetry, int? take) =>
            Results.Ok(telemetry.GetRecentFeed(take ?? 50)));

        group.MapGet("/aggregate/{nodeId}", (string nodeId, ITelemetryStore telemetry) =>
            Results.Ok(telemetry.GetAggregate(nodeId)));

        group.MapGet("/grid", (ITelemetryStore telemetry) =>
            Results.Ok(telemetry.GetGridSnapshot()));

        return group;
    }
}
