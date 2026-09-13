namespace SmartX.Shared.Enums;

/// <summary>
/// The three payload shapes an ESP32-class device publishes in the Smart-X
/// brief: floats for analog readings (soil moisture), ints for counters/
/// power metrics (wattage), and bools for discrete state (valve open/closed).
/// </summary>
public enum TelemetryDataType
{
    Float,
    Integer,
    Boolean
}
