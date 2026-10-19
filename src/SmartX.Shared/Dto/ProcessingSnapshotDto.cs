using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

/// <summary>State of the telemetry queues and the order recent messages were processed in.</summary>
public sealed class ProcessingSnapshotDto
{
    public required int NormalQueueDepth { get; init; }
    public required int PriorityQueueDepth { get; init; }
    public required bool IsPaused { get; init; }
    public required long TotalProcessed { get; init; }
    public required long TotalRejected { get; init; }
    public required int MaxConsecutivePriority { get; init; }

    /// <summary>Most recently processed first.</summary>
    public required List<ProcessedMessageDto> RecentlyProcessed { get; init; }
}

public sealed class ProcessedMessageDto
{
    /// <summary>1, 2, 3... in the order the processor handled messages.</summary>
    public required long ProcessedOrder { get; init; }

    /// <summary>1, 2, 3... in the order messages arrived.</summary>
    public required long ArrivalOrder { get; init; }
    public required string MacAddress { get; init; }
    public required string DeviceName { get; init; }
    public required string SensorType { get; init; }
    public required double Value { get; init; }
    public required ValidationSeverity Severity { get; init; }

    /// <summary>"PriorityQueue" or "Queue".</summary>
    public required string Source { get; init; }
    public required DateTimeOffset ProcessedAtUtc { get; init; }
}
