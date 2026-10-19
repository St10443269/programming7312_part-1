namespace SmartX.Shared.Dto;

/// <summary>One "Suggested Action" produced by the recommendation engine. Never executed automatically.</summary>
public sealed class RecommendationDto
{
    /// <summary>"Pattern", "RecurringFault", "OutOfRange" or "Disconnected".</summary>
    public required string Type { get; init; }
    public required string MacAddress { get; init; }
    public required string DeviceName { get; init; }
    public required string Issue { get; init; }
    public required string Reason { get; init; }
    public required string SuggestedAction { get; init; }
    public string? Evidence { get; init; }
    public required double Score { get; init; }

    /// <summary>When the suggestion is a device command, the user can choose to send it with one click.</summary>
    public DeviceCommandRequest? SuggestedCommand { get; init; }
}

public sealed class RecommendationReportDto
{
    public required bool HasEnoughPatternData { get; init; }
    public required string Summary { get; init; }
    public required int SearchesAnalysed { get; init; }
    public required int CommandsAnalysed { get; init; }
    public required int AlertsAnalysed { get; init; }
    public required List<RecommendationDto> Recommendations { get; init; }
}
