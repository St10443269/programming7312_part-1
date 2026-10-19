namespace SmartX.Shared.Dto;

public sealed class SimulatorStatusDto
{
    public required bool IsRunning { get; init; }
    public required int TickMilliseconds { get; init; }
    public required int DeviceCount { get; init; }
    public required List<string> OfflineDeviceMacs { get; init; }
}
