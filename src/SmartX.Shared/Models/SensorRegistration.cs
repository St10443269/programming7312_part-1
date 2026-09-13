using SmartX.Shared.Enums;

namespace SmartX.Shared.Models;

/// <summary>
/// A registered Smart-X device profile: identity, where it is deployed,
/// what kind of readings it produces, and any files attached to it
/// (config exports, deployment photos, hardware logs).
/// </summary>
public sealed class SensorRegistration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string MacAddress { get; init; }
    public required string FacilityName { get; init; }
    public required string ZoneName { get; init; }
    public string? SubZoneName { get; init; }
    public required string NodeId { get; init; }
    public required SensorCategory Category { get; init; }
    public DateTimeOffset RegisteredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<SensorAttachment> Attachments { get; init; } = [];

    /// <summary>Human-readable deployment path, e.g. "Facility A / Zone 1 / Sub-Zone B".</summary>
    public string DeploymentPath => string.IsNullOrWhiteSpace(SubZoneName)
        ? $"{FacilityName} / {ZoneName}"
        : $"{FacilityName} / {ZoneName} / {SubZoneName}";
}
