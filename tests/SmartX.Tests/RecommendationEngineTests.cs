using SmartX.Api.RealTime;
using SmartX.Shared.Enums;
using SmartX.Shared.Search;

namespace SmartX.Tests;

/// <summary>Recommendations come from recorded data, explain themselves, and never act on their own.</summary>
public class RecommendationEngineTests
{
    [Fact]
    public void With_no_history_it_reports_that_more_data_is_needed()
    {
        var net = new TestNetwork().WithDemoDevices();

        var report = net.Engine.Generate();

        Assert.False(report.HasEnoughPatternData);
        Assert.Contains("Not enough history", report.Summary);
        Assert.DoesNotContain(report.Recommendations, r => r.Type == "Pattern");
    }

    [Fact]
    public void Repeated_alert_then_command_becomes_a_pattern_with_a_suggested_command()
    {
        var net = new TestNetwork().WithDemoDevices();

        // Twice: moisture drops -> user searches for and switches on Pump A
        // -> moisture recovers -> user switches the pump off again.
        for (var round = 0; round < 2; round++)
        {
            net.SendAndProcess(TestNetwork.MoistureMac, 15);
            net.Clock.Advance(TimeSpan.FromSeconds(20));
            Search(net, "Pump A");
            net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);
            net.Clock.Advance(TimeSpan.FromSeconds(20));
            net.SendAndProcess(TestNetwork.MoistureMac, 45);
            net.Clock.Advance(TimeSpan.FromSeconds(10));
            net.Command(TestNetwork.PumpMac, CommandAction.TurnOff);
            net.Clock.Advance(TimeSpan.FromSeconds(60));
        }

        net.SendAndProcess(TestNetwork.MoistureMac, 15); // third drop - active now

        var report = net.Engine.Generate();
        Assert.True(report.HasEnoughPatternData);

        var pattern = Assert.Single(report.Recommendations, r => r.Type == "Pattern" && r.Issue.Contains("switched Pump A ON"));
        Assert.Equal("Moisture Sensor B", pattern.DeviceName);
        Assert.Contains("3 time(s)", pattern.Reason);
        Assert.Contains("2 of those times (67", pattern.Reason);
        Assert.Equal(TestNetwork.PumpMac, pattern.SuggestedCommand?.MacAddress);
        Assert.Equal(CommandAction.TurnOn, pattern.SuggestedCommand?.Action);

        Assert.Contains(report.Recommendations, r => r.Type == "Pattern" && r.Issue.Contains("searched for Pump A"));

        // Switching the pump OFF happened after recovery each time - not a response to the alert.
        Assert.DoesNotContain(report.Recommendations, r => r.Issue.Contains("switched Pump A OFF"));

        // Suggestions are never executed automatically.
        Assert.False(net.Device(TestNetwork.PumpMac).IsOn);
    }

    [Fact]
    public void A_single_occurrence_is_not_enough_to_call_it_a_pattern()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.SendAndProcess(TestNetwork.MoistureMac, 15);
        Search(net, "Pump A");
        Search(net, "Pump A");
        net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);

        var report = net.Engine.Generate();

        Assert.True(report.HasEnoughPatternData);
        Assert.DoesNotContain(report.Recommendations, r => r.Type == "Pattern");
    }

    [Fact]
    public void Device_with_recurring_alerts_is_flagged_at_the_threshold()
    {
        var net = new TestNetwork().WithDemoDevices();

        for (var i = 0; i < net.Options.Recommendations.RecurringFaultThreshold; i++)
        {
            net.SendAndProcess(TestNetwork.PlugMac, 1900); // spike
            net.SendAndProcess(TestNetwork.PlugMac, 150);  // recovers
        }

        var fault = Assert.Single(net.Engine.Generate().Recommendations, r => r.Type == "RecurringFault");
        Assert.Equal("Smart Plug 1", fault.DeviceName);
        Assert.Contains("POWER_SPIKE", fault.Issue);
        Assert.Equal(CommandAction.TurnOff, fault.SuggestedCommand?.Action);
    }

    [Fact]
    public void Low_moisture_reading_suggests_switching_on_the_pump_in_the_same_zone()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.SendAndProcess(TestNetwork.MoistureMac, 12);

        var outOfRange = Assert.Single(net.Engine.Generate().Recommendations, r => r.Type == "OutOfRange");

        Assert.Equal("Moisture Sensor B", outOfRange.DeviceName);
        Assert.Contains("12", outOfRange.Reason);
        Assert.Equal(TestNetwork.PumpMac, outOfRange.SuggestedCommand?.MacAddress);
        Assert.False(net.Device(TestNetwork.PumpMac).IsOn);
    }

    [Fact]
    public void Disconnected_device_gets_a_check_power_and_wifi_suggestion()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Clock.Advance(TimeSpan.FromSeconds(net.Options.Thresholds.DisconnectAfterSeconds + 1));
        net.Processor.SweepDisconnected();

        var offline = net.Engine.Generate().Recommendations.Where(r => r.Type == "Disconnected").ToList();

        Assert.Equal(4, offline.Count);
        Assert.All(offline, r => Assert.Contains("power supply and Wi-Fi", r.SuggestedAction));
    }

    [Fact]
    public void Recommendations_are_sorted_by_score()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.SendAndProcess(TestNetwork.MoistureMac, 12);
        net.SendAndProcess(TestNetwork.PlugMac, 1100); // warning-level out of range

        var scores = net.Engine.Generate().Recommendations.Select(r => r.Score).ToList();

        Assert.True(scores.Count >= 2);
        Assert.Equal(scores.OrderDescending(), scores);
    }

    private static void Search(TestNetwork net, string text)
    {
        var query = new DeviceQuery { Search = text };
        var matches = net.Registry.GetAll().Select(d => d.ToDto(net.Errors)).Where(query.Matches).Select(d => d.MacAddress).ToList();
        net.Searches.Record(query, matches);
    }
}
