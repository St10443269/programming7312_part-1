using SmartX.Shared.Search;

namespace SmartX.Api.RealTime;

/// <summary>One search the user ran on the dashboard and which devices it found.</summary>
public sealed record SearchRecord(DateTimeOffset AtUtc, string Description, string? Text, List<string> MatchedMacs, int ResultCount);

/// <summary>
/// Remembers the user's device searches, so the recommendation engine can
/// spot habits like "after Moisture Sensor B alerts, they look up Pump A".
/// </summary>
public sealed class SearchHistory(TimeProvider clock)
{
    private const int MaxRecords = 500;
    private const int MaxMatchesStored = 5;

    private readonly List<SearchRecord> _records = new();
    private readonly object _sync = new();

    public SearchRecord Record(DeviceQuery query, IReadOnlyList<string> matchedMacs)
    {
        var record = new SearchRecord(
            clock.GetUtcNow(),
            query.Describe(),
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            matchedMacs.Take(MaxMatchesStored).ToList(),
            matchedMacs.Count);

        lock (_sync)
        {
            _records.Add(record);
            if (_records.Count > MaxRecords)
            {
                _records.RemoveAt(0);
            }
        }

        return record;
    }

    /// <summary>All recorded searches, oldest first.</summary>
    public List<SearchRecord> GetAll()
    {
        lock (_sync)
        {
            return _records.ToList();
        }
    }

    public int Count
    {
        get { lock (_sync) return _records.Count; }
    }
}
