namespace SmartX.Shared.Dto;

/// <summary>
/// Part 2 wire format for one reading from a device, identified by its MAC
/// address the way an ESP32 would identify itself. The simulator sends
/// exactly this shape, so simulated and externally posted packets go
/// through the same validation and queueing.
/// </summary>
/// <remarks>
/// Every field is nullable on purpose: a malformed packet (missing MAC,
/// missing value) must reach the API's own validation and get a clear 400
/// response, rather than failing JSON binding.
/// </remarks>
public sealed class TelemetryPacketRequest
{
    public string? MacAddress { get; init; }
    public string? SensorType { get; init; }
    public double? Value { get; init; }

    /// <summary>When the device took the reading. Optional - defaults to the time it was received. Lets a device send buffered (older) readings.</summary>
    public DateTimeOffset? CapturedAtUtc { get; init; }
}
