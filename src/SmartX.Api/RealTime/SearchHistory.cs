using SmartX.Shared.Dto;
using SmartX.Shared.Search;

namespace SmartX.Api.RealTime;

/// <summary>
/// Remembers the user's device searches, so the recommendation engine can
/// spot habits like "after Moisture Sensor B alerts, they look up Pump A".
/// </summary>
public sealed class SearchHistory(TimeProvider clock)
{
    private const int MaxRecords = 500;
    private const int MaxMatchesStored = 5;

    private readonly List<SearchRecordDto> _records = new();
    private readonly object _sync = new();

    public SearchRecordDto Record(DeviceQuery query, IReadOnlyList<string> matchedMacs)
    {
        var record = new SearchRecordDto
        {
            AtUtc = clock.GetUtcNow(),
            Description = query.Describe(),
            Text = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            MatchedMacs = matchedMacs.Take(MaxMatchesStored).ToList(),
            ResultCount = matchedMacs.Count
        };

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
    public List<SearchRecordDto> GetAll()
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
