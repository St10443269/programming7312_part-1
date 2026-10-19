namespace SmartX.Shared.Enums;

/// <summary>
/// The kinds of simulated hardware on the Part 2 device network: ESP32
/// controllers, smart plugs, pumps/relays (switchable actuators) and
/// environmental/power sensors (read-only).
/// </summary>
public enum DeviceKind
{
    Esp32Controller,
    MoistureSensor,
    PowerMeter,
    SmartPlug,
    WaterPump,
    Relay
}
