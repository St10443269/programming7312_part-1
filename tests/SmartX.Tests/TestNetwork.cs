using SmartX.Api.RealTime;
using SmartX.Api.Services;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>A clock the tests move by hand, so timestamps are exact and repeatable.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// The same Part 2 services the API registers in Program.cs, wired together
/// by hand with a fake clock - no web server, simulator or background
/// workers, so each test controls exactly when messages are processed.
/// </summary>
public sealed class TestNetwork
{
    public const string MoistureMac = "24:6F:28:B1:00:01";
    public const string PumpMac = "24:6F:28:A1:00:01";
    public const string PlugMac = "24:6F:28:D3:00:01";
    public const string MeterMac = "24:6F:28:F3:00:01";

    public TestNetwork(Action<RealTimeOptions>? configure = null)
    {
        Options = new RealTimeOptions();
        configure?.Invoke(Options);

        Clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        Registry = new DeviceRegistry(Clock);
        History = new HistoryLog(Options);
        Errors = new ActiveErrorTracker();
        Classifier = new TelemetryClassifier(Options);
        Broadcaster = new LiveUpdateBroadcaster();
        Processor = new TelemetryProcessor(Registry, History, Errors, Classifier, Broadcaster, new TelemetryStore(), Options, Clock);
        Commands = new CommandService(Registry, Errors, History, Broadcaster, Clock);
        Searches = new SearchHistory(Clock);
        Engine = new RecommendationEngine(Registry, History, Errors, Commands, Searches, Classifier, Options, Clock);
    }

    public RealTimeOptions Options { get; }
    public FakeClock Clock { get; }
    public DeviceRegistry Registry { get; }
    public HistoryLog History { get; }
    public ActiveErrorTracker Errors { get; }
    public TelemetryClassifier Classifier { get; }
    public LiveUpdateBroadcaster Broadcaster { get; }
    public TelemetryProcessor Processor { get; }
    public CommandService Commands { get; }
    public SearchHistory Searches { get; }
    public RecommendationEngine Engine { get; }

    /// <summary>Moisture sensor + pump in Zone 1, a smart plug and power meter in Zone 3.</summary>
    public TestNetwork WithDemoDevices()
    {
        AddDevice(MoistureMac, "Moisture Sensor B", DeviceKind.MoistureSensor, "Zone 1");
        AddDevice(PumpMac, "Pump A", DeviceKind.WaterPump, "Zone 1");
        AddDevice(PlugMac, "Smart Plug 1", DeviceKind.SmartPlug, "Zone 3", isOn: true);
        AddDevice(MeterMac, "Power Meter M1", DeviceKind.PowerMeter, "Zone 3");
        return this;
    }

    public SimulatedDevice AddDevice(string mac, string name, DeviceKind kind, string zone, bool isOn = false)
    {
        var device = new SimulatedDevice(Guid.NewGuid(), mac, name, kind, zone, isOn);
        Assert.True(Registry.TryRegister(device));
        return device;
    }

    public SimulatedDevice Device(string mac)
    {
        Assert.True(Registry.TryGet(mac, out var device));
        return device;
    }

    /// <summary>Queues a reading for a device using that device's own sensor type.</summary>
    public EnqueueResult Send(string mac, double value, DateTimeOffset? capturedAt = null) =>
        Processor.Enqueue(new TelemetryPacketRequest
        {
            MacAddress = mac,
            SensorType = Device(mac).SensorType,
            Value = value,
            CapturedAtUtc = capturedAt
        });

    /// <summary>Queues a reading and processes everything waiting.</summary>
    public void SendAndProcess(string mac, double value)
    {
        Assert.True(Send(mac, value).Accepted);
        ProcessAll();
    }

    public int ProcessAll() => Processor.ProcessPending(int.MaxValue);

    /// <summary>Device names in the order the processor handled them (oldest first).</summary>
    public List<(string Device, string Source, ValidationSeverity Severity)> ProcessingOrder() =>
        Processor.GetSnapshot().RecentlyProcessed
            .OrderBy(m => m.ProcessedOrder)
            .Select(m => (m.DeviceName, m.Source, m.Severity))
            .ToList();

    public CommandRecordDto Command(string mac, CommandAction action) =>
        Commands.Execute(new DeviceCommandRequest { MacAddress = mac, Action = action });
}
