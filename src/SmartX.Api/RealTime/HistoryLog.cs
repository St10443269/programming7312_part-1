using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>
/// Sort key for the history log: the timestamp first, then the arrival
/// sequence number. Two readings captured at exactly the same instant get
/// different keys (different sequence numbers), so neither overwrites the
/// other, while the log still sorts chronologically.
/// </summary>
public readonly record struct HistoryKey(DateTimeOffset TimestampUtc, long Sequence) : IComparable<HistoryKey>
{
    public int CompareTo(HistoryKey other)
    {
        var byTime = TimestampUtc.CompareTo(other.TimestampUtc);
        return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
    }
}

/// <summary>
/// Chronological log of readings, commands, alerts and recoveries, held in a
/// <see cref="SortedDictionary{TKey,TValue}"/> keyed by <see cref="HistoryKey"/>.
/// </summary>
/// <remarks>
/// A SortedDictionary keeps its keys in order as they are inserted (it is a
/// balanced binary search tree, O(log n) per insert; Microsoft, n.d.f). Readings do not always
/// arrive in time order - a device can send buffered readings late, with an
/// older capture time - but they still land in the right place, so the
/// dashboard's timeline and chart can read the log front-to-back without
/// re-sorting it on every request. Removing the oldest entry when the cap is
/// reached is also cheap, because the oldest is always the first key.
/// </remarks>
public sealed class HistoryLog(RealTimeOptions options)
{
    private readonly SortedDictionary<HistoryKey, HistoryEntryDto> _entries = new();
    private readonly object _sync = new();
    private long _nextSequence;

    public HistoryEntryDto Add(
        DateTimeOffset timestampUtc, HistoryEntryKind kind, string macAddress, string deviceName, string text,
        ValidationSeverity severity = ValidationSeverity.Info, string? sensorType = null, double? value = null, string? code = null)
    {
        lock (_sync)
        {
            var sequence = ++_nextSequence;
            var entry = new HistoryEntryDto
            {
                Sequence = sequence,
                TimestampUtc = timestampUtc,
                Kind = kind,
                MacAddress = macAddress,
                DeviceName = deviceName,
                SensorType = sensorType,
                Value = value,
                Severity = severity,
                Code = code,
                Text = text
            };

            _entries.Add(new HistoryKey(timestampUtc, sequence), entry);

            // Memory cap: drop the oldest entry (always the first key).
            while (_entries.Count > options.Processing.MaxHistoryEntries)
            {
                _entries.Remove(_entries.Keys.First());
            }

            return entry;
        }
    }

    /// <summary>
    /// Entries in chronological order (oldest first), optionally filtered.
    /// <paramref name="take"/> keeps the most recent N of the matches, still
    /// returned oldest-first so they can be drawn left-to-right on a chart.
    /// </summary>
    public List<HistoryEntryDto> Query(
        string? macAddress = null, HistoryEntryKind? kind = null, DateTimeOffset? fromUtc = null, int take = 200)
    {
        var mac = string.IsNullOrWhiteSpace(macAddress) ? null : DeviceRegistry.NormaliseMac(macAddress);

        lock (_sync)
        {
            // Enumerating a SortedDictionary walks it in key order, so the
            // result is already chronological - no OrderBy needed.
            var matches = _entries.Values.Where(e =>
                (mac is null || e.MacAddress == mac) &&
                (kind is null || e.Kind == kind) &&
                (fromUtc is null || e.TimestampUtc >= fromUtc));

            var list = matches.ToList();
            return list.Count > take ? list.GetRange(list.Count - take, take) : list;
        }
    }

    public int Count
    {
        get { lock (_sync) return _entries.Count; }
    }
}
