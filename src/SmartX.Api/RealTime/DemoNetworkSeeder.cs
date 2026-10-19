using SmartX.Api.Services;
using SmartX.Shared.Enums;
using SmartX.Shared.Models;

namespace SmartX.Api.RealTime;

/// <summary>
/// Registers a small SIMULATED device network at startup so the dashboard
/// has something to show without any real hardware. Each device is added
/// to both the Part 2 <see cref="DeviceRegistry"/> and the Part 1
/// <see cref="ISensorRepository"/> (same id and MAC), so the Part 1 Sensors
/// and Telemetry pages see the same devices.
/// </summary>
public static class DemoNetworkSeeder
{
    // 24:6F:28 is an Espressif (ESP32) MAC prefix; the rest is made up.
    private static readonly (string Mac, string Name, DeviceKind Kind, string Zone, bool IsOn)[] DemoDevices =
    [
        ("24:6F:28:B1:00:01", "Moisture Sensor B", DeviceKind.MoistureSensor, "Zone 1", false),
        ("24:6F:28:A1:00:01", "Pump A", DeviceKind.WaterPump, "Zone 1", false),
        ("24:6F:28:C1:00:01", "ESP32 Controller Z1", DeviceKind.Esp32Controller, "Zone 1", false),
        ("24:6F:28:B2:00:01", "Moisture Sensor D", DeviceKind.MoistureSensor, "Zone 2", false),
        ("24:6F:28:A2:00:01", "Pump C", DeviceKind.WaterPump, "Zone 2", true),
        ("24:6F:28:D3:00:01", "Smart Plug 1", DeviceKind.SmartPlug, "Zone 3", true),
        ("24:6F:28:D3:00:02", "Smart Plug 2", DeviceKind.SmartPlug, "Zone 3", true),
        ("24:6F:28:E3:00:01", "Relay R1", DeviceKind.Relay, "Zone 3", false),
        ("24:6F:28:F3:00:01", "Power Meter M1", DeviceKind.PowerMeter, "Zone 3", false)
    ];

    public static void Seed(DeviceRegistry registry, ISensorRepository sensors)
    {
        foreach (var (mac, name, kind, zone, isOn) in DemoDevices)
        {
            var device = new SimulatedDevice(Guid.NewGuid(), mac, name, kind, zone, isOn);
            if (!registry.TryRegister(device))
            {
                continue;
            }

            sensors.Add(new SensorRegistration
            {
                Id = device.Id,
                MacAddress = device.MacAddress,
                FacilityName = "Facility A",
                ZoneName = zone,
                NodeId = name,
                Category = device.Category
            });
        }
    }

    /// <summary>Part 1 registrations only carry a category; pick the matching simulated device kind.</summary>
    public static DeviceKind KindForCategory(SensorCategory category) => category switch
    {
        SensorCategory.Environmental => DeviceKind.MoistureSensor,
        SensorCategory.PowerConsumption => DeviceKind.PowerMeter,
        _ => DeviceKind.Relay
    };
}
