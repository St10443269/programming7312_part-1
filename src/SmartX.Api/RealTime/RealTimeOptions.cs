namespace SmartX.Api.RealTime;

/// <summary>
/// Every tunable number for Part 2 in one place, bound from the "RealTime"
/// section of appsettings.json so thresholds can be changed without
/// touching code. The defaults here are what the app uses if a value is
/// missing from the file.
/// </summary>
public sealed class RealTimeOptions
{
    public ThresholdOptions Thresholds { get; set; } = new();
    public ProcessingOptions Processing { get; set; } = new();
    public SimulatorOptions Simulator { get; set; } = new();
    public RecommendationOptions Recommendations { get; set; } = new();

    public sealed class ThresholdOptions
    {
        /// <summary>Soil moisture below this is a Critical alert (priority 0).</summary>
        public double MoistureCriticalPct { get; set; } = 20;

        /// <summary>Soil moisture below this is a Warning (priority 1).</summary>
        public double MoistureWarningPct { get; set; } = 30;

        /// <summary>Power draw above this is a Critical power spike (priority 0).</summary>
        public double PowerSpikeWatts { get; set; } = 1500;

        /// <summary>Power draw above this is a Warning (priority 1).</summary>
        public double PowerWarningWatts { get; set; } = 1000;

        /// <summary>Controller temperature above this is a Critical overheat (priority 0).</summary>
        public double TemperatureCriticalC { get; set; } = 40;

        /// <summary>Controller temperature above this is a Warning (priority 1).</summary>
        public double TemperatureWarningC { get; set; } = 35;

        /// <summary>A device that has sent nothing for this long is marked Disconnected.</summary>
        public int DisconnectAfterSeconds { get; set; } = 8;
    }

    public sealed class ProcessingOptions
    {
        /// <summary>How often the background worker drains the queues.</summary>
        public int IntervalMilliseconds { get; set; } = 300;

        /// <summary>Most messages handled per worker tick.</summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>
        /// Starvation guard: after this many priority messages in a row, one
        /// normal message is processed (if any are waiting) before priority
        /// processing continues, so normal telemetry is never ignored forever.
        /// </summary>
        public int MaxConsecutivePriority { get; set; } = 5;

        /// <summary>Oldest history entries are dropped beyond this many (memory cap for the in-memory log).</summary>
        public int MaxHistoryEntries { get; set; } = 5000;
    }

    public sealed class SimulatorOptions
    {
        public bool StartOnLaunch { get; set; } = true;
        public int TickMilliseconds { get; set; } = 1000;

        /// <summary>Chance per device per tick of a random dropout, to exercise disconnect handling.</summary>
        public double RandomDisconnectChance { get; set; } = 0.002;
    }

    public sealed class RecommendationOptions
    {
        /// <summary>A command or search this soon after an alert counts as a response to it.</summary>
        public int PatternWindowMinutes { get; set; } = 5;

        /// <summary>A trigger -> response pair must be seen at least this many times to become a pattern.</summary>
        public int MinPatternOccurrences { get; set; } = 2;

        /// <summary>...and must follow at least this fraction of that trigger's alerts (0-1).</summary>
        public double MinPatternConfidence { get; set; } = 0.5;

        /// <summary>Below this many recorded searches + commands, pattern analysis reports "not enough data".</summary>
        public int MinHistoryForPatterns { get; set; } = 3;

        /// <summary>This many alerts of the same type for one device within the lookback window is a recurring fault.</summary>
        public int RecurringFaultThreshold { get; set; } = 3;

        public int LookbackMinutes { get; set; } = 60;
    }
}
