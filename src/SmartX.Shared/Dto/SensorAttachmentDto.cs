namespace SmartX.Shared.Dto;

public sealed class SensorAttachmentDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset UploadedAtUtc { get; init; }
}
