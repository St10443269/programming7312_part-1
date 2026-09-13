using SmartX.Shared.Dto;

namespace SmartX.Api.Services;

public interface ITelemetryStore
{
    TelemetryEventDto IngestFloat(Guid deviceId, string deviceLabel, string sensorType, float value);
    TelemetryEventDto IngestInt(Guid deviceId, string deviceLabel, string sensorType, int value, int zoneIndex);
    TelemetryEventDto IngestBool(Guid deviceId, string deviceLabel, string sensorType, bool value);

    /// <summary>Flattens a jagged batch of sequential float samples (one array per device) into stored packets.</summary>
    List<TelemetryEventDto> IngestFloatBatch(Guid[] deviceIds, string[] deviceLabels, string sensorType, float[][] samples);

    IReadOnlyList<TelemetryEventDto> GetRecentFeed(int take = 50);
    AggregateResultDto GetAggregate(string nodeId);
    GridSnapshotDto GetGridSnapshot();

    /// <summary>Maps a zone name onto one of the fixed heatmap rows, e.g. "Zone 2" -&gt; row 1.</summary>
    int ResolveZoneIndex(string zoneName);
}
