namespace SmartX.Shared.Dto;

public sealed class ActiveErrorDto
{
    public required string MacAddress { get; init; }
    public required string DeviceName { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset SinceUtc { get; init; }

    /// <summary>How many repeat reports of this same error were ignored because it was already active.</summary>
    public required int SuppressedDuplicates { get; init; }
}
