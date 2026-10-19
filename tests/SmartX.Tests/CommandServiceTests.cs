using SmartX.Api.RealTime;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>Manual commands and the Stack&lt;T&gt;-based undo.</summary>
public class CommandServiceTests
{
    [Fact]
    public void Accepted_command_changes_the_simulated_device_and_is_pushed_onto_the_stack()
    {
        var net = new TestNetwork().WithDemoDevices();

        var result = net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);

        Assert.Equal(CommandOutcome.Accepted, result.Outcome);
        Assert.True(net.Device(TestNetwork.PumpMac).IsOn);
        Assert.Equal(result.Id, net.Commands.PeekUndo()?.Id);
        Assert.Single(net.History.Query(TestNetwork.PumpMac, HistoryEntryKind.Command));
    }

    [Fact]
    public void Undo_reverts_the_most_recent_command_first()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);
        net.Command(TestNetwork.PlugMac, CommandAction.TurnOff);

        Assert.Equal(["Smart Plug 1", "Pump A"], net.Commands.GetUndoStack().Select(c => c.DeviceName));

        var first = net.Commands.Undo();
        Assert.Equal(CommandOutcome.Accepted, first.Outcome);
        Assert.Equal("Smart Plug 1", first.DeviceName);
        Assert.True(net.Device(TestNetwork.PlugMac).IsOn);   // back ON
        Assert.True(net.Device(TestNetwork.PumpMac).IsOn);   // untouched so far

        var second = net.Commands.Undo();
        Assert.Equal("Pump A", second.DeviceName);
        Assert.False(net.Device(TestNetwork.PumpMac).IsOn);

        Assert.Equal(CommandOutcome.Rejected, net.Commands.Undo().Outcome); // stack now empty
    }

    [Fact]
    public void Undo_itself_is_not_pushed_so_undoing_twice_does_not_redo()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);

        net.Commands.Undo();

        Assert.Empty(net.Commands.GetUndoStack());
        Assert.False(net.Device(TestNetwork.PumpMac).IsOn);
    }

    [Fact]
    public void Commands_to_sensors_or_unknown_devices_are_rejected_without_changes()
    {
        var net = new TestNetwork().WithDemoDevices();

        Assert.Equal(CommandOutcome.Rejected, net.Command(TestNetwork.MoistureMac, CommandAction.TurnOn).Outcome);
        Assert.Equal(CommandOutcome.Rejected, net.Command("24:6F:28:00:00:99", CommandAction.TurnOn).Outcome);
        Assert.Equal(CommandOutcome.Rejected, net.Command("", CommandAction.TurnOn).Outcome);
        Assert.Equal(CommandOutcome.Rejected, net.Command(TestNetwork.PumpMac, (CommandAction)42).Outcome);
        Assert.Empty(net.Commands.GetUndoStack());
        Assert.Equal(4, net.Commands.GetLog().Count); // still recorded in the history
    }

    [Fact]
    public void Command_that_changes_nothing_is_accepted_but_not_undoable()
    {
        var net = new TestNetwork().WithDemoDevices();

        var result = net.Command(TestNetwork.PlugMac, CommandAction.TurnOn); // plug starts ON

        Assert.Equal(CommandOutcome.Accepted, result.Outcome);
        Assert.Contains("already ON", result.Message);
        Assert.Empty(net.Commands.GetUndoStack());
    }

    [Fact]
    public void Command_to_a_disconnected_device_fails_and_changes_nothing()
    {
        var net = new TestNetwork().WithDemoDevices();
        Disconnect(net, TestNetwork.PumpMac);

        var result = net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);

        Assert.Equal(CommandOutcome.Failed, result.Outcome);
        Assert.False(net.Device(TestNetwork.PumpMac).IsOn);
        Assert.Empty(net.Commands.GetUndoStack());
    }

    [Fact]
    public void Undo_while_the_device_is_disconnected_fails_and_keeps_the_entry_for_later()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);
        Disconnect(net, TestNetwork.PumpMac);

        Assert.Equal(CommandOutcome.Failed, net.Commands.Undo().Outcome);
        Assert.Single(net.Commands.GetUndoStack());

        net.SendAndProcess(TestNetwork.PumpMac, 12); // reconnects (still running)
        Assert.Equal(CommandOutcome.Accepted, net.Commands.Undo().Outcome);
        Assert.False(net.Device(TestNetwork.PumpMac).IsOn);
    }

    [Fact]
    public void Undo_is_refused_when_the_device_changed_after_the_command()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Command(TestNetwork.PlugMac, CommandAction.TurnOff);

        // A newer reading shows the plug drawing power again (switched on locally).
        net.Clock.Advance(TimeSpan.FromSeconds(5));
        net.SendAndProcess(TestNetwork.PlugMac, 180);
        Assert.True(net.Device(TestNetwork.PlugMac).IsOn);

        var undo = net.Commands.Undo();

        Assert.Equal(CommandOutcome.Rejected, undo.Outcome);
        Assert.Contains("has changed since", undo.Message);
        Assert.Empty(net.Commands.GetUndoStack()); // stale entry discarded
    }

    [Fact]
    public void Reading_captured_before_a_command_does_not_overwrite_the_new_state()
    {
        var net = new TestNetwork().WithDemoDevices();
        net.Send(TestNetwork.PumpMac, 0); // queued while the pump was off...

        net.Clock.Advance(TimeSpan.FromMilliseconds(500));
        net.Command(TestNetwork.PumpMac, CommandAction.TurnOn);
        net.ProcessAll(); // ...processed after the pump was switched on

        Assert.True(net.Device(TestNetwork.PumpMac).IsOn);
    }

    private static void Disconnect(TestNetwork net, string mac)
    {
        net.Clock.Advance(TimeSpan.FromSeconds(net.Options.Thresholds.DisconnectAfterSeconds + 1));
        net.Processor.SweepDisconnected();
        Assert.False(net.Device(mac).IsConnected);
    }
}
