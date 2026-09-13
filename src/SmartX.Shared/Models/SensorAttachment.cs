namespace SmartX.Shared.Models;

/// <summary>
/// A file attached to a sensor profile - a device config export, a
/// deployment photo, or a hardware log - uploaded via the API's
/// multipart file endpoint.
/// </summary>
public sealed class SensorAttachment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SensorId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string StoragePath { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
