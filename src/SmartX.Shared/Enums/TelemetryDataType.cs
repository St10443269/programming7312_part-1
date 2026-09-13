namespace SmartX.Shared.Enums;

/// <summary>
/// The three payload shapes an ESP32-class device publishes in the Smart-X
/// brief: floats for analog readings (soil moisture) off the ADC, ints for
/// counters/power metrics (wattage), and bools for discrete GPIO state
/// (valve open/closed) - matching the ADC/GPIO capabilities described in
/// Espressif Systems (n.d.).
/// </summary>
public enum TelemetryDataType
{
    Float,
    Integer,
    Boolean
}
