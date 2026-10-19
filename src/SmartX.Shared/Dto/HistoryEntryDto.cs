using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

/// <summary>One entry in the chronological history log (a reading, command, alert or recovery).</summary>
public sealed class HistoryEntryDto
{
    /// <summary>Arrival order - breaks ties between entries with the same timestamp.</summary>
    public required long Sequence { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required HistoryEntryKind Kind { get; init; }
    public required string MacAddress { get; init; }
    public required string DeviceName { get; init; }
    public string? SensorType { get; init; }
    public double? Value { get; init; }
    public required ValidationSeverity Severity { get; init; }

    /// <summary>Error code for Alert/Recovery entries, e.g. "POWER_SPIKE".</summary>
    public string? Code { get; init; }
    public required string Text { get; init; }
}
