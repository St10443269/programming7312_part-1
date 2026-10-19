using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>
/// Decides how serious a reading is, using the configurable thresholds.
/// The severity decides which queue the message goes into:
/// <list type="bullet">
///   <item>Critical -> priority queue, priority 0 (and raises an active error)</item>
///   <item>Warning -> priority queue, priority 1</item>
///   <item>Info -> normal FIFO queue</item>
/// </list>
/// </summary>
public sealed class TelemetryClassifier(RealTimeOptions options)
{
    public const string MoistureLow = "MOISTURE_LOW";
    public const string PowerSpike = "POWER_SPIKE";
    public const string Overheat = "OVERHEAT";
    public const string Disconnected = "DISCONNECTED";

    public readonly record struct Classification(ValidationSeverity Severity, string? AlertCode, string? AlertText);

    /// <summary>Lower number = processed sooner. Only Warning/Critical messages use the priority queue.</summary>
    public static int PriorityFor(ValidationSeverity severity) => severity == ValidationSeverity.Critical ? 0 : 1;

    public Classification Classify(string sensorType, double value)
    {
        var t = options.Thresholds;
        return sensorType switch
        {
            "SoilMoisturePct" when value < t.MoistureCriticalPct =>
                new(ValidationSeverity.Critical, MoistureLow, $"Critical moisture drop: {value:0.0}% (below {t.MoistureCriticalPct}%)"),
            "SoilMoisturePct" when value < t.MoistureWarningPct =>
                new(ValidationSeverity.Warning, null, null),

            "PowerWatts" when value > t.PowerSpikeWatts =>
                new(ValidationSeverity.Critical, PowerSpike, $"Severe power spike: {value:0} W (above {t.PowerSpikeWatts} W)"),
            "PowerWatts" when value > t.PowerWarningWatts =>
                new(ValidationSeverity.Warning, null, null),

            "TemperatureC" when value > t.TemperatureCriticalC =>
                new(ValidationSeverity.Critical, Overheat, $"Controller overheating: {value:0.0} °C (above {t.TemperatureCriticalC} °C)"),
            "TemperatureC" when value > t.TemperatureWarningC =>
                new(ValidationSeverity.Warning, null, null),

            _ => new(ValidationSeverity.Info, null, null)
        };
    }

    /// <summary>The error code this sensor type can raise, if any - used to clear it again on recovery.</summary>
    public static string? AlertCodeFor(string sensorType) => sensorType switch
    {
        "SoilMoisturePct" => MoistureLow,
        "PowerWatts" => PowerSpike,
        "TemperatureC" => Overheat,
        _ => null
    };

    /// <summary>
    /// Physically impossible values mean a malformed packet, not an alert.
    /// Returns an error message, or null if the value is plausible.
    /// </summary>
    public static string? ValidateRange(string sensorType, double value) => sensorType switch
    {
        "SoilMoisturePct" when value is < 0 or > 100 => "Soil moisture must be between 0 and 100%.",
        "PowerWatts" when value < 0 => "Power draw cannot be negative.",
        "FlowLitresPerMin" when value < 0 => "Flow rate cannot be negative.",
        "RelayClosed" when value is not (0 or 1) => "Relay state must be 0 (open) or 1 (closed).",
        "TemperatureC" when value is < -40 or > 125 => "Temperature is outside the ESP32's operating range (-40 to 125 °C).",
        _ => null
    };
}
