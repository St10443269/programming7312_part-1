using SmartX.Api.RealTime;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>Queue&lt;T&gt; (FIFO), PriorityQueue (alerts first), starvation guard, and packet validation.</summary>
public class TelemetryProcessorTests
{
    [Fact]
    public void Normal_messages_are_processed_first_in_first_out()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.Send(TestNetwork.PumpMac, 0);
        net.Send(TestNetwork.MoistureMac, 45);
        net.Send(TestNetwork.PlugMac, 150);
        net.ProcessAll();

        var order = net.ProcessingOrder();
        Assert.Equal(["Pump A", "Moisture Sensor B", "Smart Plug 1"], order.Select(o => o.Device));
        Assert.All(order, o => Assert.Equal(TelemetryProcessor.NormalQueueSource, o.Source));
    }

    [Fact]
    public void Critical_alert_is_processed_before_normal_messages_queued_ahead_of_it()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.Send(TestNetwork.PumpMac, 0);
        net.Send(TestNetwork.MoistureMac, 45);
        var spike = net.Send(TestNetwork.PlugMac, 1900); // severe power spike, sent last
        net.ProcessAll();

        Assert.Equal(ValidationSeverity.Critical, spike.Queued!.Severity);
        var order = net.ProcessingOrder();
        Assert.Equal(("Smart Plug 1", TelemetryProcessor.PriorityQueueSource, ValidationSeverity.Critical), order[0]);
        // ...and the normal messages are still processed afterwards, in FIFO order.
        Assert.Equal(["Pump A", "Moisture Sensor B"], order.Skip(1).Select(o => o.Device));
    }

    [Fact]
    public void Critical_beats_warning_and_equal_priorities_keep_arrival_order()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.Send(TestNetwork.MoistureMac, 25);  // warning (priority 1)
        net.Send(TestNetwork.PlugMac, 1800);    // critical (priority 0)
        net.Send(TestNetwork.MeterMac, 1600);   // critical (priority 0), arrived after the plug
        net.ProcessAll();

        Assert.Equal(["Smart Plug 1", "Power Meter M1", "Moisture Sensor B"], net.ProcessingOrder().Select(o => o.Device));
    }

    [Fact]
    public void Starvation_guard_lets_a_normal_message_through_after_max_consecutive_alerts()
    {
        var net = new TestNetwork(o => o.Processing.MaxConsecutivePriority = 2).WithDemoDevices();

        net.Send(TestNetwork.PumpMac, 0); // the one normal message
        for (var i = 0; i < 4; i++)
        {
            net.Send(TestNetwork.PlugMac, 1700 + i);
        }

        net.ProcessAll();

        var sources = net.ProcessingOrder().Select(o => o.Source == TelemetryProcessor.PriorityQueueSource ? "P" : "N");
        Assert.Equal(["P", "P", "N", "P", "P"], sources);
    }

    [Fact]
    public void Unknown_device_is_rejected_with_404_and_nothing_is_queued()
    {
        var net = new TestNetwork().WithDemoDevices();

        var result = net.Processor.Enqueue(new TelemetryPacketRequest { MacAddress = "24:6F:28:00:00:99", SensorType = "PowerWatts", Value = 10 });

        Assert.False(result.Accepted);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(0, net.ProcessAll());
        Assert.Equal(1, net.Processor.GetSnapshot().TotalRejected);
    }

    [Theory]
    [InlineData(null, "SoilMoisturePct", 40.0, "MacAddress is required")]
    [InlineData("not-a-mac", "SoilMoisturePct", 40.0, "not a valid MAC")]
    [InlineData(TestNetwork.MoistureMac, null, 40.0, "SensorType is required")]
    [InlineData(TestNetwork.MoistureMac, "SoilMoisturePct", null, "Value is required")]
    [InlineData(TestNetwork.MoistureMac, "SoilMoisturePct", double.NaN, "finite")]
    [InlineData(TestNetwork.MoistureMac, "PowerWatts", 40.0, "reports 'SoilMoisturePct'")]
    [InlineData(TestNetwork.MoistureMac, "SoilMoisturePct", 150.0, "between 0 and 100")]
    public void Malformed_packets_are_rejected_with_400(string? mac, string? sensorType, double? value, string expectedMessage)
    {
        var net = new TestNetwork().WithDemoDevices();

        var result = net.Processor.Enqueue(new TelemetryPacketRequest { MacAddress = mac, SensorType = sensorType, Value = value });

        Assert.False(result.Accepted);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(expectedMessage, result.Message);
    }

    [Fact]
    public void Paused_processing_leaves_messages_waiting_in_both_queues()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Processor.SetPaused(true);

        net.Send(TestNetwork.PumpMac, 0);
        net.Send(TestNetwork.PlugMac, 1900);

        var snapshot = net.Processor.GetSnapshot();
        Assert.True(snapshot.IsPaused);
        Assert.Equal(1, snapshot.NormalQueueDepth);
        Assert.Equal(1, snapshot.PriorityQueueDepth);
    }

    [Fact]
    public void Processed_reading_updates_the_device()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.SendAndProcess(TestNetwork.MoistureMac, 42.5);

        var device = net.Device(TestNetwork.MoistureMac);
        Assert.Equal(42.5, device.LastReading);
        Assert.Equal(net.Clock.Now, device.LastSeenUtc);
        Assert.Equal(DeviceStatus.Active, device.ToDto(net.Errors).Status);
    }
}
