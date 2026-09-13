namespace SmartX.Shared.Enums;

/// <summary>
/// Broad classification of a registered Smart-X device, used to route
/// telemetry to the correct <see cref="TelemetryDataType"/> and to group
/// devices on the dashboard.
/// </summary>
public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}
