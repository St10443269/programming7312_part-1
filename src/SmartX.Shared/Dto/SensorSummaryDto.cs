using SmartX.Shared.Enums;

namespace SmartX.Shared.Dto;

public sealed class SensorSummaryDto
{
    public required Guid Id { get; init; }
    public required string MacAddress { get; init; }
    public required string DeploymentPath { get; init; }
    public required string NodeId { get; init; }
    public required SensorCategory Category { get; init; }
    public required DateTimeOffset RegisteredAtUtc { get; init; }
    public required int AttachmentCount { get; init; }
}
