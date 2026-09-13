namespace SmartX.Shared.Dto;

/// <summary>
/// A snapshot of the server-side zone-load matrix (a rectangular
/// <c>double[,]</c> internally) reshaped into a jagged array for JSON
/// transport, feeding the dashboard's live grid-load heatmap.
/// </summary>
public sealed class GridSnapshotDto
{
    public required string[] ZoneNames { get; init; }
    public required double[][] WattsByZoneThenSlot { get; init; }
    public required double MaxWatts { get; init; }
    public required double[] LatestWattsByZone { get; init; }
}
