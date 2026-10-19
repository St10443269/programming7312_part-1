using SmartX.Shared.Dto;

namespace SmartX.Api.RealTime;

/// <summary>Identity of one active error: which device, and what is wrong with it.</summary>
public readonly record struct ActiveErrorKey(string MacAddress, string Code);

/// <summary>
/// The set of errors that are active right now, held in a
/// <see cref="HashSet{T}"/> of (MAC, error code) pairs.
/// </summary>
/// <remarks>
/// A device in trouble keeps reporting the same bad reading every second.
/// Without de-duplication each of those would raise a fresh alert. Because a
/// HashSet only holds unique values, <see cref="HashSet{T}.Add"/> returns
/// false when that exact (device, error) is already present - an O(1)
/// check - so the processor raises the alert once and just counts the
/// repeats. When the device recovers the entry is removed, so the set
/// always means "currently active", not "ever happened" (past alerts stay
/// in the <see cref="HistoryLog"/>). A record struct is used as the
/// element type because it gets value-based equality and hashing for free.
/// </remarks>
public sealed class ActiveErrorTracker
{
    private readonly HashSet<ActiveErrorKey> _active = new();

    // Extra details shown on the dashboard. The set alone decides whether
    // an error is active; this dictionary only holds its message, start time
    // and duplicate count.
    private readonly Dictionary<ActiveErrorKey, (string DeviceName, string Message, DateTimeOffset SinceUtc, int Duplicates)> _details = new();
    private readonly object _sync = new();

    /// <summary>
    /// Reports an error. Returns true if it is new (the caller should raise
    /// an alert), or false if the same error was already active (a duplicate,
    /// which is counted but not re-alerted).
    /// </summary>
    public bool Report(string macAddress, string deviceName, string code, string message, DateTimeOffset nowUtc)
    {
        var key = new ActiveErrorKey(macAddress, code);
        lock (_sync)
        {
            if (_active.Add(key))
            {
                _details[key] = (deviceName, message, nowUtc, 0);
                return true;
            }

            var existing = _details[key];
            _details[key] = existing with { Message = message, Duplicates = existing.Duplicates + 1 };
            return false;
        }
    }

    /// <summary>Clears an error when the device recovers. Returns true if it was active.</summary>
    public bool Resolve(string macAddress, string code)
    {
        var key = new ActiveErrorKey(macAddress, code);
        lock (_sync)
        {
            if (!_active.Remove(key))
            {
                return false;
            }

            _details.Remove(key);
            return true;
        }
    }

    public bool IsActive(string macAddress, string code)
    {
        lock (_sync)
        {
            return _active.Contains(new ActiveErrorKey(macAddress, code));
        }
    }

    public List<string> CodesFor(string macAddress)
    {
        lock (_sync)
        {
            return _active.Where(k => k.MacAddress == macAddress).Select(k => k.Code).OrderBy(c => c).ToList();
        }
    }

    public List<ActiveErrorDto> GetAll()
    {
        lock (_sync)
        {
            return _active
                .Select(key =>
                {
                    var detail = _details[key];
                    return new ActiveErrorDto
                    {
                        MacAddress = key.MacAddress,
                        DeviceName = detail.DeviceName,
                        Code = key.Code,
                        Message = detail.Message,
                        SinceUtc = detail.SinceUtc,
                        SuppressedDuplicates = detail.Duplicates
                    };
                })
                .OrderBy(e => e.SinceUtc)
                .ToList();
        }
    }

    public int Count
    {
        get { lock (_sync) return _active.Count; }
    }
}
