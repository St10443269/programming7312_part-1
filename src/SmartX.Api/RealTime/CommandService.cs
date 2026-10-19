using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>
/// Executes manual override commands and keeps the undo history in a
/// <see cref="Stack{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Undo always means "reverse the most recent command", which is exactly
/// Last-In-First-Out: every accepted command is pushed, and Undo pops the top
/// one and restores the device's previous state. Push, Peek and Pop are all
/// O(1) (Microsoft, n.d.d).
/// </para>
/// <para>
/// <b>Limitation - state may have moved on.</b> Between the command and the
/// undo, the device's real state can change (e.g. a pump reports it has
/// stopped, or the reading shows the plug was switched off locally). Undo
/// therefore only reverts if the device is still in the state the command
/// put it in; otherwise it is refused and that entry is discarded, because
/// "restoring" it would overwrite a newer, real change. Undo is also
/// refused (and the entry kept for a later retry) while the device is
/// disconnected. An undo is not itself pushed, so pressing Undo twice
/// reverts the last two commands rather than redoing the first.
/// </para>
/// </remarks>
public sealed class CommandService(
    DeviceRegistry registry,
    ActiveErrorTracker errors,
    HistoryLog history,
    LiveUpdateBroadcaster broadcaster,
    TimeProvider clock)
{
    private const int MaxLogEntries = 200;

    private readonly Stack<CommandRecordDto> _undoStack = new();
    private readonly List<CommandRecordDto> _log = new();
    private readonly object _sync = new();

    public CommandRecordDto Execute(DeviceCommandRequest request)
    {
        lock (_sync)
        {
            var now = clock.GetUtcNow();
            var mac = string.IsNullOrWhiteSpace(request.MacAddress) ? "" : DeviceRegistry.NormaliseMac(request.MacAddress);

            if (!Enum.IsDefined(request.Action))
            {
                return Record(Rejected(mac, mac, request.Action, now, $"'{request.Action}' is not a supported action."));
            }

            if (!registry.TryGet(mac, out var device))
            {
                return Record(Rejected(mac, mac, request.Action, now,
                    string.IsNullOrEmpty(mac) ? "A target device MAC address is required." : $"Unknown device '{mac}'."));
            }

            if (!device.IsControllable)
            {
                return Record(Rejected(mac, device.Name, request.Action, now,
                    $"{device.Name} is a {device.Kind} - read-only sensors cannot be switched on or off."));
            }

            if (!device.IsConnected)
            {
                return Record(new CommandRecordDto
                {
                    Id = Guid.NewGuid(), MacAddress = mac, DeviceName = device.Name, Action = request.Action,
                    Outcome = CommandOutcome.Failed, IssuedAtUtc = now,
                    Message = $"{device.Name} is disconnected, so the command could not be delivered. Nothing was changed."
                });
            }

            var turnOn = request.Action == CommandAction.TurnOn;
            if (device.IsOn == turnOn)
            {
                // Valid, but there is nothing to change - and so nothing to undo.
                return Record(new CommandRecordDto
                {
                    Id = Guid.NewGuid(), MacAddress = mac, DeviceName = device.Name, Action = request.Action,
                    Outcome = CommandOutcome.Accepted, IssuedAtUtc = now, PreviousIsOn = turnOn, NewIsOn = turnOn,
                    Message = $"{device.Name} is already {(turnOn ? "ON" : "OFF")} - no change made."
                });
            }

            var previous = device.SetPower(turnOn, now);
            var record = new CommandRecordDto
            {
                Id = Guid.NewGuid(), MacAddress = mac, DeviceName = device.Name, Action = request.Action,
                Outcome = CommandOutcome.Accepted, IssuedAtUtc = now, PreviousIsOn = previous, NewIsOn = turnOn,
                Message = $"{device.Name} switched {(turnOn ? "ON" : "OFF")}."
            };

            _undoStack.Push(record);
            return Record(record, device);
        }
    }

    public CommandRecordDto Undo()
    {
        lock (_sync)
        {
            var now = clock.GetUtcNow();

            if (_undoStack.Count == 0)
            {
                return Record(new CommandRecordDto
                {
                    Id = Guid.NewGuid(), MacAddress = "", DeviceName = "-", Action = CommandAction.TurnOff,
                    Outcome = CommandOutcome.Rejected, IssuedAtUtc = now, IsUndo = true,
                    Message = "Nothing to undo - the command stack is empty."
                });
            }

            var last = _undoStack.Peek();
            var revertAction = last.PreviousIsOn == true ? CommandAction.TurnOn : CommandAction.TurnOff;

            if (!registry.TryGet(last.MacAddress, out var device))
            {
                _undoStack.Pop();
                return Record(UndoResult(last, revertAction, CommandOutcome.Rejected, now,
                    $"{last.DeviceName} is no longer registered, so its command was discarded."));
            }

            if (!device.IsConnected)
            {
                // Keep it on the stack - it can be undone once the device is back.
                return Record(UndoResult(last, revertAction, CommandOutcome.Failed, now,
                    $"{device.Name} is disconnected; undo can be retried when it reconnects."));
            }

            _undoStack.Pop();

            if (device.IsOn != last.NewIsOn)
            {
                return Record(UndoResult(last, revertAction, CommandOutcome.Rejected, now,
                    $"{device.Name} has changed since that command (it is now {(device.IsOn ? "ON" : "OFF")}), so it was not reverted. The entry was discarded."));
            }

            var restoreTo = last.PreviousIsOn ?? false;
            device.SetPower(restoreTo, now);
            return Record(UndoResult(last, revertAction, CommandOutcome.Accepted, now,
                $"Undid \"{last.Action}\" on {device.Name} - restored to {(restoreTo ? "ON" : "OFF")}.",
                previousIsOn: last.NewIsOn, newIsOn: restoreTo), device);
        }
    }

    /// <summary>The command Undo would revert next, without removing it.</summary>
    public CommandRecordDto? PeekUndo()
    {
        lock (_sync)
        {
            return _undoStack.Count > 0 ? _undoStack.Peek() : null;
        }
    }

    /// <summary>The stack's contents, top (most recent) first.</summary>
    public List<CommandRecordDto> GetUndoStack()
    {
        lock (_sync)
        {
            return _undoStack.ToList(); // Stack enumerates from the top down.
        }
    }

    /// <summary>Every command and undo attempt (including rejected ones), newest first.</summary>
    public List<CommandRecordDto> GetLog(int take = 50)
    {
        lock (_sync)
        {
            return Enumerable.Reverse(_log).Take(Math.Clamp(take, 1, MaxLogEntries)).ToList();
        }
    }

    private CommandRecordDto Record(CommandRecordDto record, SimulatedDevice? changedDevice = null)
    {
        _log.Add(record);
        if (_log.Count > MaxLogEntries)
        {
            _log.RemoveAt(0);
        }

        if (record.Outcome == CommandOutcome.Accepted && changedDevice is not null)
        {
            var entry = history.Add(record.IssuedAtUtc, HistoryEntryKind.Command, record.MacAddress, record.DeviceName,
                record.IsUndo ? $"Undo: {record.Message}" : $"Command {record.Action}: {record.Message}");

            broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.HistoryType, Entry = entry });
            broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.DeviceType, Device = changedDevice.ToDto(errors) });
        }

        broadcaster.Publish(new LiveUpdateDto { Type = LiveUpdateDto.CommandType, Command = record });
        return record;
    }

    private static CommandRecordDto Rejected(string mac, string deviceName, CommandAction action, DateTimeOffset now, string message) => new()
    {
        Id = Guid.NewGuid(), MacAddress = mac, DeviceName = deviceName, Action = action,
        Outcome = CommandOutcome.Rejected, IssuedAtUtc = now, Message = message
    };

    private static CommandRecordDto UndoResult(
        CommandRecordDto original, CommandAction revertAction, CommandOutcome outcome, DateTimeOffset now, string message,
        bool? previousIsOn = null, bool? newIsOn = null) => new()
    {
        Id = Guid.NewGuid(), MacAddress = original.MacAddress, DeviceName = original.DeviceName, Action = revertAction,
        Outcome = outcome, IssuedAtUtc = now, Message = message, IsUndo = true, UndoOfCommandId = original.Id,
        PreviousIsOn = previousIsOn, NewIsOn = newIsOn
    };
}
