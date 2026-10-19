namespace SmartX.Api.RealTime;

/// <summary>
/// The registered devices, held in a <see cref="Dictionary{TKey,TValue}"/>
/// keyed by MAC address.
/// </summary>
/// <remarks>
/// Every incoming telemetry packet identifies its device by MAC, so the
/// lookup on the hot path is "which device is this MAC?". A dictionary is a
/// hash table, so that lookup is O(1) on average (Microsoft, n.d.e) however many devices are
/// registered, where searching a List would be O(n) per packet. MACs are
/// normalised (upper-case, ':' separators) before being used as keys so
/// "24-6f-28-..." and "24:6F:28:..." find the same device.
/// </remarks>
public sealed class DeviceRegistry(TimeProvider clock)
{
    private readonly Dictionary<string, SimulatedDevice> _devicesByMac = new();
    private readonly Dictionary<string, DateTimeOffset> _registeredAtByMac = new();
    private readonly object _sync = new();

    public static string NormaliseMac(string macAddress) =>
        macAddress.Trim().ToUpperInvariant().Replace('-', ':');

    /// <summary>Adds the device. Returns false (and changes nothing) if that MAC is already registered.</summary>
    public bool TryRegister(SimulatedDevice device)
    {
        lock (_sync)
        {
            if (!_devicesByMac.TryAdd(device.MacAddress, device))
            {
                return false;
            }

            _registeredAtByMac[device.MacAddress] = clock.GetUtcNow();
            return true;
        }
    }

    /// <summary>O(1) lookup by MAC. Returns false for an unknown (unregistered) device instead of throwing.</summary>
    public bool TryGet(string? macAddress, out SimulatedDevice device)
    {
        device = null!;
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return false;
        }

        lock (_sync)
        {
            return _devicesByMac.TryGetValue(NormaliseMac(macAddress), out device!);
        }
    }

    public bool Contains(string macAddress)
    {
        lock (_sync)
        {
            return _devicesByMac.ContainsKey(NormaliseMac(macAddress));
        }
    }

    public IReadOnlyList<SimulatedDevice> GetAll()
    {
        lock (_sync)
        {
            return _devicesByMac.Values.OrderBy(d => d.ZoneName).ThenBy(d => d.Name).ToList();
        }
    }

    public DateTimeOffset GetRegisteredAt(string macAddress)
    {
        lock (_sync)
        {
            return _registeredAtByMac.GetValueOrDefault(NormaliseMac(macAddress), clock.GetUtcNow());
        }
    }

    public int Count
    {
        get { lock (_sync) return _devicesByMac.Count; }
    }
}
