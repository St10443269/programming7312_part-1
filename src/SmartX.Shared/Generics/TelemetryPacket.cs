namespace SmartX.Shared.Generics;

/// <summary>
/// Uniform, type-safe envelope for a single telemetry reading coming off a
/// gateway device. Instead of writing three near-identical classes for
/// float/int/bool payloads (or storing everything as <c>object</c>, which
/// boxes every value-type reading on a resource-constrained device), a
/// single generic definition is reused for every payload shape.
/// </summary>
/// <remarks>
/// <para>
/// The <c>where T : struct</c> constraint is the important part: it limits
/// <typeparamref name="T"/> to value types (float, int, bool, etc.), which
/// is what lets the JIT build a dedicated, closed generic type per
/// <c>T</c> (e.g. <c>TelemetryPacket&lt;float&gt;</c>) instead of erasing
/// to a shared reference-type implementation (Microsoft, 2026). The
/// reading is stored directly in the struct's own memory layout - never
/// coerced into <c>object</c> - so there is no boxing/unboxing overhead
/// (Microsoft, 2025) when thousands of packets a second are queued for
/// ingestion.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The value type carried by this packet, e.g. <see cref="float"/> for a
/// soil-moisture reading, <see cref="int"/> for a wattage counter, or
/// <see cref="bool"/> for a valve/relay state.
/// </typeparam>
public sealed class TelemetryPacket<T> where T : struct
{
    public Guid DeviceId { get; }
    public string SensorType { get; }
    public T Value { get; }
    public DateTimeOffset CapturedAtUtc { get; }

    public TelemetryPacket(Guid deviceId, string sensorType, T value, DateTimeOffset? capturedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sensorType))
        {
            throw new ArgumentException("Sensor type must be provided.", nameof(sensorType));
        }

        DeviceId = deviceId;
        SensorType = sensorType;
        Value = value;
        CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Formats the reading for display without the caller needing to know
    /// the concrete type of <typeparamref name="T"/>.
    /// </summary>
    public string FormatValue() => Value switch
    {
        float f => f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "ON" : "OFF",
        _ => Convert.ToString(Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
    };

    public override string ToString() => $"[{CapturedAtUtc:HH:mm:ss}] {SensorType} = {FormatValue()}";
}
