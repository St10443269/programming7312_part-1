using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>
/// One device on the simulated network. No physical hardware is attached:
/// <see cref="DeviceSimulator"/> produces its readings and commands change
/// <see cref="IsOn"/> directly. Mutable state is guarded by a per-device
/// lock because the processor, the simulator and command requests can all
/// touch the same device at once.
/// </summary>
public sealed class SimulatedDevice
{
    private readonly object _sync = new();
    private bool _isOn;
    private bool _isConnected = true;
    private double? _lastReading;
    private DateTimeOffset? _lastSeenUtc;

    public SimulatedDevice(Guid id, string macAddress, string name, DeviceKind kind, string zoneName, bool isOn = false)
    {
        Id = id;
        MacAddress = DeviceRegistry.NormaliseMac(macAddress);
        Name = name;
        Kind = kind;
        ZoneName = zoneName;
        _isOn = isOn;
    }

    public Guid Id { get; }
    public string MacAddress { get; }
    public string Name { get; }
    public DeviceKind Kind { get; }
    public string ZoneName { get; }
    public bool IsSimulated => true;

    public bool IsControllable => Kind is DeviceKind.SmartPlug or DeviceKind.WaterPump or DeviceKind.Relay;

    public SensorCategory Category => Kind switch
    {
        DeviceKind.MoistureSensor or DeviceKind.Esp32Controller => SensorCategory.Environmental,
        DeviceKind.PowerMeter or DeviceKind.SmartPlug => SensorCategory.PowerConsumption,
        _ => SensorCategory.Actuator
    };

    /// <summary>The one reading type this device reports.</summary>
    public string SensorType => SensorTypeFor(Kind);

    public string Unit => Kind switch
    {
        DeviceKind.MoistureSensor => "%",
        DeviceKind.PowerMeter or DeviceKind.SmartPlug => "W",
        DeviceKind.WaterPump => "L/min",
        DeviceKind.Esp32Controller => "°C",
        _ => ""
    };

    public static string SensorTypeFor(DeviceKind kind) => kind switch
    {
        DeviceKind.MoistureSensor => "SoilMoisturePct",
        DeviceKind.PowerMeter or DeviceKind.SmartPlug => "PowerWatts",
        DeviceKind.WaterPump => "FlowLitresPerMin",
        DeviceKind.Relay => "RelayClosed",
        _ => "TemperatureC"
    };

    public bool IsOn { get { lock (_sync) return _isOn; } }
    public bool IsConnected { get { lock (_sync) return _isConnected; } }
    public double? LastReading { get { lock (_sync) return _lastReading; } }
    public DateTimeOffset? LastSeenUtc { get { lock (_sync) return _lastSeenUtc; } }

    /// <summary>Sets the on/off state. Returns the previous state.</summary>
    public bool SetPower(bool isOn)
    {
        lock (_sync)
        {
            var previous = _isOn;
            _isOn = isOn;
            return previous;
        }
    }

    /// <summary>
    /// Records a processed reading. A switchable device's reading also
    /// reveals its real on/off state (a pump reporting flow is running),
    /// so that state is updated from the reading too - which is how undo
    /// can tell that a device changed after a command was sent.
    /// </summary>
    public void ApplyReading(double value, DateTimeOffset seenAtUtc)
    {
        lock (_sync)
        {
            _lastReading = value;
            if (_lastSeenUtc is null || seenAtUtc > _lastSeenUtc)
            {
                _lastSeenUtc = seenAtUtc;
            }

            _isConnected = true;

            switch (Kind)
            {
                case DeviceKind.SmartPlug: _isOn = value > 5; break;
                case DeviceKind.WaterPump: _isOn = value > 0.5; break;
                case DeviceKind.Relay: _isOn = value >= 0.5; break;
            }
        }
    }

    /// <summary>Marks the device disconnected if it has been silent since <paramref name="cutoffUtc"/>. Returns true if this call changed it.</summary>
    public bool MarkDisconnectedIfSilentSince(DateTimeOffset cutoffUtc, DateTimeOffset registeredAtUtc)
    {
        lock (_sync)
        {
            var lastContact = _lastSeenUtc ?? registeredAtUtc;
            if (!_isConnected || lastContact >= cutoffUtc)
            {
                return false;
            }

            _isConnected = false;
            return true;
        }
    }

    public DeviceDto ToDto(IReadOnlyCollection<string> activeErrorCodes)
    {
        lock (_sync)
        {
            var status = !_isConnected
                ? DeviceStatus.Disconnected
                : activeErrorCodes.Count > 0 ? DeviceStatus.Fault : DeviceStatus.Active;

            return new DeviceDto
            {
                Id = Id,
                MacAddress = MacAddress,
                Name = Name,
                Kind = Kind,
                Category = Category,
                ZoneName = ZoneName,
                IsSimulated = IsSimulated,
                IsControllable = IsControllable,
                IsOn = _isOn,
                Status = status,
                SensorType = SensorType,
                Unit = Unit,
                LastReading = _lastReading,
                LastSeenUtc = _lastSeenUtc,
                ActiveErrorCodes = activeErrorCodes.ToList()
            };
        }
    }
}
