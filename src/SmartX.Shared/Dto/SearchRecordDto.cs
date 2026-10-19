namespace SmartX.Shared.Dto;

/// <summary>One device search the user ran, and which devices it found.</summary>
public sealed class SearchRecordDto
{
    public required DateTimeOffset AtUtc { get; init; }

    /// <summary>Readable summary of the search, e.g. "'pump' + status=Active".</summary>
    public required string Description { get; init; }
    public string? Text { get; init; }
    public required List<string> MatchedMacs { get; init; }
    public required int ResultCount { get; init; }
}
