namespace SmartX.Shared.Enums;

public enum DeviceStatus
{
    /// <summary>Reporting normally with no active errors.</summary>
    Active,

    /// <summary>Still reporting, but has at least one active error (e.g. moisture critically low).</summary>
    Fault,

    /// <summary>No telemetry received within the disconnect timeout.</summary>
    Disconnected
}
