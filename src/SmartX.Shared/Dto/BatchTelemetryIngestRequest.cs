namespace SmartX.Shared.Dto;

/// <summary>
/// A sequential historical batch of raw readings per device, e.g. ten
/// soil-moisture samples an ESP32 buffered before it reconnected. The
/// outer index is the device, the inner arrays are that device's
/// sequential samples - a genuine jagged array, since devices rarely
/// buffer the same number of readings.
/// </summary>
public sealed class BatchTelemetryIngestRequest
{
    public required Guid[] DeviceIds { get; init; }
    public required string SensorType { get; init; }
    public required float[][] Samples { get; init; }
}
