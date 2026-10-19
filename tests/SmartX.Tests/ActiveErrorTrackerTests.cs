using SmartX.Api.RealTime;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>HashSet of active errors: duplicates suppressed, removed on recovery.</summary>
public class ActiveErrorTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_error_reported_twice_is_new_once_then_counted_as_a_duplicate()
    {
        var errors = new ActiveErrorTracker();

        Assert.True(errors.Report("MAC1", "Plug", "POWER_SPIKE", "spike", Now));
        Assert.False(errors.Report("MAC1", "Plug", "POWER_SPIKE", "spike again", Now));
        Assert.False(errors.Report("MAC1", "Plug", "POWER_SPIKE", "and again", Now));

        var active = Assert.Single(errors.GetAll());
        Assert.Equal(2, active.SuppressedDuplicates);
    }

    [Fact]
    public void Different_codes_or_devices_are_separate_errors()
    {
        var errors = new ActiveErrorTracker();

        Assert.True(errors.Report("MAC1", "Plug", "POWER_SPIKE", "", Now));
        Assert.True(errors.Report("MAC1", "Plug", "DISCONNECTED", "", Now));
        Assert.True(errors.Report("MAC2", "Meter", "POWER_SPIKE", "", Now));

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Resolved_error_is_no_longer_active_and_can_be_reported_again()
    {
        var errors = new ActiveErrorTracker();
        errors.Report("MAC1", "Plug", "POWER_SPIKE", "", Now);

        Assert.True(errors.Resolve("MAC1", "POWER_SPIKE"));
        Assert.False(errors.IsActive("MAC1", "POWER_SPIKE"));
        Assert.False(errors.Resolve("MAC1", "POWER_SPIKE")); // already gone
        Assert.True(errors.Report("MAC1", "Plug", "POWER_SPIKE", "", Now)); // new again
    }

    [Fact]
    public void Repeated_critical_readings_raise_one_alert_and_recovery_clears_it()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.SendAndProcess(TestNetwork.MoistureMac, 15);
        net.SendAndProcess(TestNetwork.MoistureMac, 14);
        net.SendAndProcess(TestNetwork.MoistureMac, 13);

        Assert.Single(net.History.Query(kind: HistoryEntryKind.Alert));
        var error = Assert.Single(net.Errors.GetAll());
        Assert.Equal(TelemetryClassifier.MoistureLow, error.Code);
        Assert.Equal(2, error.SuppressedDuplicates);
        Assert.Equal(DeviceStatus.Fault, net.Device(TestNetwork.MoistureMac).ToDto(net.Errors).Status);

        net.SendAndProcess(TestNetwork.MoistureMac, 45);

        Assert.Empty(net.Errors.GetAll());
        Assert.Single(net.History.Query(kind: HistoryEntryKind.Recovery));
        Assert.Equal(DeviceStatus.Active, net.Device(TestNetwork.MoistureMac).ToDto(net.Errors).Status);
    }

    [Fact]
    public void Warning_level_reading_does_not_clear_a_critical_error_yet()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.SendAndProcess(TestNetwork.MoistureMac, 15); // critical
        net.SendAndProcess(TestNetwork.MoistureMac, 25); // only a warning - still dry

        Assert.True(net.Errors.IsActive(TestNetwork.MoistureMac, TelemetryClassifier.MoistureLow));
    }

    [Fact]
    public void Silent_device_is_marked_disconnected_once_and_reconnects_on_its_next_reading()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.SendAndProcess(TestNetwork.PumpMac, 0);

        net.Clock.Advance(TimeSpan.FromSeconds(net.Options.Thresholds.DisconnectAfterSeconds + 1));
        net.Processor.SweepDisconnected();
        net.Processor.SweepDisconnected(); // a second sweep must not duplicate the alert

        Assert.True(net.Errors.IsActive(TestNetwork.PumpMac, TelemetryClassifier.Disconnected));
        Assert.Equal(DeviceStatus.Disconnected, net.Device(TestNetwork.PumpMac).ToDto(net.Errors).Status);
        Assert.Single(net.History.Query(TestNetwork.PumpMac, HistoryEntryKind.Alert));

        net.SendAndProcess(TestNetwork.PumpMac, 0);

        Assert.False(net.Errors.IsActive(TestNetwork.PumpMac, TelemetryClassifier.Disconnected));
        Assert.Equal(DeviceStatus.Active, net.Device(TestNetwork.PumpMac).ToDto(net.Errors).Status);
    }
}
