using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.Services;

/// <summary>
/// Generates plausible mock telemetry for every registered device so the
/// ingestion pipeline, pulse feed, and grid heatmap can be demonstrated
/// under load without needing real ESP32 hardware attached.
/// </summary>
public sealed class MockTelemetrySeeder(ISensorRepository sensors, ITelemetryStore telemetry)
{
    private readonly Random _random = new();

    public List<TelemetryEventDto> SeedRandomBatch(int count)
    {
        var registered = sensors.GetAll();
        var events = new List<TelemetryEventDto>();

        if (registered.Count == 0)
        {
            return events;
        }

        for (var i = 0; i < count; i++)
        {
            var sensor = registered[_random.Next(registered.Count)];
            var zoneIndex = telemetry.ResolveZoneIndex(sensor.ZoneName);

            var dto = sensor.Category switch
            {
                SensorCategory.Environmental => telemetry.IngestFloat(
                    sensor.Id, sensor.NodeId, "SoilMoisturePct", NextMoisture()),

                SensorCategory.PowerConsumption => telemetry.IngestInt(
                    sensor.Id, sensor.NodeId, "PowerWatts", NextWatts(), zoneIndex),

                SensorCategory.Actuator => telemetry.IngestBool(
                    sensor.Id, sensor.NodeId, "ValveOpen", NextValveState()),

                _ => throw new InvalidOperationException($"Unhandled sensor category: {sensor.Category}")
            };

            events.Add(dto);
        }

        return events;
    }

    private float NextMoisture()
    {
        // Occasional spike/drop simulates a genuine anomaly for the pulse feed to flag.
        var isAnomaly = _random.NextDouble() < 0.12;
        return isAnomaly
            ? (float)(_random.NextDouble() * 100)
            : 40f + (float)(_random.NextDouble() * 20 - 10);
    }

    private int NextWatts()
    {
        var isSpike = _random.NextDouble() < 0.1;
        return isSpike
            ? _random.Next(800, 2200)
            : _random.Next(60, 400);
    }

    private bool NextValveState() => _random.NextDouble() < 0.3;
}
