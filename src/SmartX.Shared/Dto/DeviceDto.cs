using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

/// <summary>Current state of one device on the (simulated) Part 2 network, as shown on the live dashboard.</summary>
public sealed class DeviceDto
{
    public required Guid Id { get; init; }
    public required string MacAddress { get; init; }
    public required string Name { get; init; }
    public required DeviceKind Kind { get; init; }
    public required SensorCategory Category { get; init; }
    public required string ZoneName { get; init; }

    /// <summary>Always true in this project - no physical hardware is connected.</summary>
    public required bool IsSimulated { get; init; }

    /// <summary>True for smart plugs, pumps and relays, which accept on/off commands.</summary>
    public required bool IsControllable { get; init; }
    public required bool IsOn { get; init; }

    public required DeviceStatus Status { get; init; }
    public required string SensorType { get; init; }
    public required string Unit { get; init; }
    public double? LastReading { get; init; }
    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>Codes of this device's currently active errors, e.g. "MOISTURE_LOW".</summary>
    public required List<string> ActiveErrorCodes { get; init; }
}
