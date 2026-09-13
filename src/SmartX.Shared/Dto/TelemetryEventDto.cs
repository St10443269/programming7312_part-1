using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

/// <summary>
/// A telemetry reading shaped for the live "Telemetry Pulse" feed on the
/// dashboard, including the anomaly severity computed server-side from the
/// delta against the previous reading for that device/sensor pair.
/// </summary>
public sealed class TelemetryEventDto
{
    public required Guid DeviceId { get; init; }
    public required string DeviceLabel { get; init; }
    public required string SensorType { get; init; }
    public required string DisplayValue { get; init; }
    public required ValidationSeverity Severity { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
