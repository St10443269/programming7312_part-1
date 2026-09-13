namespace SmartX.Shared.Dto;

/// <summary>Result of aggregating (via overloaded operators) every power reading for a node over the current batch.</summary>
public sealed class AggregateResultDto
{
    public required string NodeId { get; init; }
    public required double TotalWatts { get; init; }
    public required double DeltaFromPreviousWatts { get; init; }
    public required int SampleCount { get; init; }
}
