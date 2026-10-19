using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>
/// SIMULATION ONLY - stands in for the physical ESP32 network. Every tick it
/// produces one reading per online device and submits it through
/// <see cref="TelemetryProcessor.Enqueue"/>, exactly like an external device
/// posting to /api/stream/telemetry, so simulated data gets no shortcuts.
/// </summary>
/// <remarks>
/// Readings depend on the devices' real state, so commands have visible
/// effects: a running pump raises soil moisture in its zone (and moisture
/// falls while it is off), a switched-off smart plug draws ~0 W, and the
/// zone power meter adds up the plugs. Devices can also drop offline,
/// randomly or on request, to exercise disconnect handling.
/// </remarks>
public sealed class DeviceSimulator(
    DeviceRegistry registry,
    TelemetryProcessor processor,
    RealTimeOptions options,
    ILogger<DeviceSimulator> logger) : BackgroundService
{
    private sealed class SimState
    {
        public double Moisture = 45;
        public double DryingPerTick = 0.6;
        public double LastWatts;
        public int OfflineTicksRemaining;
    }

    private readonly Dictionary<string, SimState> _states = new();
    private readonly object _sync = new();
    private readonly Random _random = new();
    private volatile bool _isRunning = options.Simulator.StartOnLaunch;

    public bool IsRunning => _isRunning;

    public void SetRunning(bool running) => _isRunning = running;

    /// <summary>Makes a device stop reporting for a while, as if it lost power or Wi-Fi.</summary>
    public bool Disconnect(string macAddress, int seconds)
    {
        if (!registry.TryGet(macAddress, out var device))
        {
            return false;
        }

        var ticks = Math.Max(1, seconds * 1000 / Math.Max(100, options.Simulator.TickMilliseconds));
        lock (_sync)
        {
            StateFor(device).OfflineTicksRemaining = ticks;
        }

        return true;
    }

    public SimulatorStatusDto GetStatus()
    {
        lock (_sync)
        {
            return new SimulatorStatusDto
            {
                IsRunning = _isRunning,
                TickMilliseconds = options.Simulator.TickMilliseconds,
                DeviceCount = registry.Count,
                OfflineDeviceMacs = _states.Where(s => s.Value.OfflineTicksRemaining > 0).Select(s => s.Key).ToList()
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100, options.Simulator.TickMilliseconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_isRunning)
            {
                continue;
            }

            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Simulator tick failed; continuing.");
            }
        }
    }

    /// <summary>One round of readings. Public so tests can drive the simulator step by step.</summary>
    public void Tick()
    {
        var devices = registry.GetAll();
        var packets = new List<TelemetryPacketRequest>();

        lock (_sync)
        {
            foreach (var device in devices)
            {
                var state = StateFor(device);
                if (state.OfflineTicksRemaining > 0)
                {
                    state.OfflineTicksRemaining--;
                    continue;
                }

                if (_random.NextDouble() < options.Simulator.RandomDisconnectChance)
                {
                    state.OfflineTicksRemaining = _random.Next(10, 25);
                    continue;
                }

                packets.Add(new TelemetryPacketRequest
                {
                    MacAddress = device.MacAddress,
                    SensorType = device.SensorType,
                    Value = Math.Round(NextValue(device, state, devices), 2)
                });
            }
        }

        foreach (var packet in packets)
        {
            processor.Enqueue(packet);
        }
    }

    private double NextValue(SimulatedDevice device, SimState state, IReadOnlyList<SimulatedDevice> devices)
    {
        switch (device.Kind)
        {
            case DeviceKind.MoistureSensor:
                var pumpRunning = devices.Any(d => d.Kind == DeviceKind.WaterPump && d.ZoneName == device.ZoneName && d.IsOn);
                state.Moisture += pumpRunning ? 2.5 : -state.DryingPerTick;
                state.Moisture = Math.Clamp(state.Moisture + Noise(0.3), 5, 90);
                return state.Moisture;

            case DeviceKind.WaterPump:
                return device.IsOn ? 12 + Noise(0.5) : 0;

            case DeviceKind.SmartPlug:
                if (!device.IsOn)
                {
                    state.LastWatts = _random.NextDouble() * 1.5;
                }
                else
                {
                    var spike = _random.NextDouble() < 0.01;
                    state.LastWatts = spike ? _random.Next(1600, 2100) : _random.Next(120, 300);
                }

                return state.LastWatts;

            case DeviceKind.Relay:
                return device.IsOn ? 1 : 0;

            case DeviceKind.PowerMeter:
                var plugLoad = devices
                    .Where(d => d.Kind == DeviceKind.SmartPlug && d.ZoneName == device.ZoneName)
                    .Sum(d => _states.TryGetValue(d.MacAddress, out var s) ? s.LastWatts : 0);
                return 150 + _random.Next(0, 100) + plugLoad;

            default: // ESP32 controller board temperature
                var overheat = _random.NextDouble() < 0.015;
                return overheat ? 41 + _random.NextDouble() * 3 : 24 + Noise(1.5);
        }
    }

    private SimState StateFor(SimulatedDevice device)
    {
        if (!_states.TryGetValue(device.MacAddress, out var state))
        {
            state = new SimState();

            // "Moisture Sensor B" dries out quickly and starts lowish, so its
            // critical-drop alert (and the pump response to it) shows up soon
            // after launch; other sensors start comfortably moist.
            if (device.Kind == DeviceKind.MoistureSensor && device.Name.EndsWith(" B", StringComparison.Ordinal))
            {
                state.Moisture = 34;
                state.DryingPerTick = 1.1;
            }
            else
            {
                state.Moisture = 55;
                state.DryingPerTick = 0.4;
            }

            _states[device.MacAddress] = state;
        }

        return state;
    }

    private double Noise(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;
}
