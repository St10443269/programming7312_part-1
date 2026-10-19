namespace SmartX.Shared.Enums;

public enum CommandOutcome
{
    /// <summary>The device state was changed.</summary>
    Accepted,

    /// <summary>The request was invalid (unknown device, read-only sensor, nothing to undo, ...). Nothing changed.</summary>
    Rejected,

    /// <summary>The request was valid but could not be delivered (e.g. the device is disconnected). Nothing changed.</summary>
    Failed
}
