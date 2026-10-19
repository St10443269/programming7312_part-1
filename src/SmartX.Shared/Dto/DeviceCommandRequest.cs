using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

public sealed class DeviceCommandRequest
{
    public string? MacAddress { get; init; }
    public CommandAction Action { get; init; }
}
