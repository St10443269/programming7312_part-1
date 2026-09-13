namespace SmartX.Shared.Aggregation;

/// <summary>
/// A single power-consumption sample from a smart meter node, expressed in
/// watts. Operators are overloaded so the dashboard and API can combine or
/// compare readings the same way it would combine two numbers, instead of
/// reaching for a static <c>MeterReading.Add(a, b)</c> helper everywhere.
/// </summary>
public readonly struct MeterReading : IEquatable<MeterReading>, IComparable<MeterReading>
{
    public string NodeId { get; }
    public double Watts { get; }
    public DateTimeOffset SampledAtUtc { get; }

    public MeterReading(string nodeId, double watts, DateTimeOffset? sampledAtUtc = null)
    {
        NodeId = nodeId;
        Watts = watts;
        SampledAtUtc = sampledAtUtc ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Aggregates the load of two meters, e.g. <c>Meter3 = Meter1 + Meter2</c>.
    /// The resulting node id records provenance instead of silently picking one side.
    /// </summary>
    public static MeterReading operator +(MeterReading a, MeterReading b) =>
        new($"{a.NodeId}+{b.NodeId}", a.Watts + b.Watts);

    /// <summary>
    /// Computes the delta load between two meters (e.g. drop-off after a
    /// device is switched off). Result can be negative.
    /// </summary>
    public static MeterReading operator -(MeterReading a, MeterReading b) =>
        new($"{a.NodeId}-{b.NodeId}", a.Watts - b.Watts);

    public static bool operator ==(MeterReading a, MeterReading b) => a.Equals(b);
    public static bool operator !=(MeterReading a, MeterReading b) => !a.Equals(b);
    public static bool operator >(MeterReading a, MeterReading b) => a.Watts > b.Watts;
    public static bool operator <(MeterReading a, MeterReading b) => a.Watts < b.Watts;
    public static bool operator >=(MeterReading a, MeterReading b) => a.Watts >= b.Watts;
    public static bool operator <=(MeterReading a, MeterReading b) => a.Watts <= b.Watts;

    public bool Equals(MeterReading other) =>
        NodeId == other.NodeId && Watts.Equals(other.Watts);

    public override bool Equals(object? obj) => obj is MeterReading other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(NodeId, Watts);

    public int CompareTo(MeterReading other) => Watts.CompareTo(other.Watts);

    public override string ToString() => $"{NodeId}: {Watts:0.0} W";
}
