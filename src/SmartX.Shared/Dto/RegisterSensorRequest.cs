using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

public sealed class RegisterSensorRequest
{
    public required string MacAddress { get; init; }
    public required string FacilityName { get; init; }
    public required string ZoneName { get; init; }
    public string? SubZoneName { get; init; }
    public required string NodeId { get; init; }
    public required SensorCategory Category { get; init; }
}
