namespace SmartX.Shared.Dto;

/// <summary>
/// One Server-Sent Events message pushed from the API to the dashboard.
/// <see cref="Type"/> says which of the optional payloads is filled in.
/// </summary>
public sealed class LiveUpdateDto
{
    public const string DeviceType = "device";
    public const string HistoryType = "history";
    public const string CommandType = "command";
    public const string ErrorsType = "errors";
    public const string ProcessingType = "processing";
    public const string HeartbeatType = "heartbeat";

    public required string Type { get; init; }
    public DeviceDto? Device { get; init; }
    public HistoryEntryDto? Entry { get; init; }
    public CommandRecordDto? Command { get; init; }
    public List<ActiveErrorDto>? Errors { get; init; }
    public ProcessingSnapshotDto? Processing { get; init; }
}
