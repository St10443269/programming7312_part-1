using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

/// <summary>One manual command (or undo) and what happened to it.</summary>
public sealed class CommandRecordDto
{
    public required Guid Id { get; init; }
    public required string MacAddress { get; init; }
    public required string DeviceName { get; init; }
    public required CommandAction Action { get; init; }
    public required CommandOutcome Outcome { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset IssuedAtUtc { get; init; }

    /// <summary>Device state before and after - only set when the command was accepted.</summary>
    public bool? PreviousIsOn { get; init; }
    public bool? NewIsOn { get; init; }

    /// <summary>True when this record is an undo of an earlier command (<see cref="UndoOfCommandId"/>).</summary>
    public bool IsUndo { get; init; }
    public Guid? UndoOfCommandId { get; init; }
}
