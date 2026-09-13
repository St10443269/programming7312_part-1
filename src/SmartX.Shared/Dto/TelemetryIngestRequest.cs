using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

/// <summary>
/// Wire-format telemetry payload. JSON has no concept of "generic type
/// parameter", so the ingest request carries a <see cref="DataType"/>
/// discriminator plus one populated value slot; the API maps this onto the
/// correctly-typed <c>TelemetryPacket&lt;T&gt;</c> server-side.
/// </summary>
public sealed class TelemetryIngestRequest
{
    public required Guid DeviceId { get; init; }
    public required string SensorType { get; init; }
    public required TelemetryDataType DataType { get; init; }
    public float? FloatValue { get; init; }
    public int? IntValue { get; init; }
    public bool? BoolValue { get; init; }
}
