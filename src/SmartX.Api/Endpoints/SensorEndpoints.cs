using SmartX.Api.Services;
using SmartX.Shared.Dto;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    private const long MaxAttachmentBytes = 25 * 1024 * 1024; // 25 MB
    private static readonly HashSet<string> AllowedExtensions =
        [".json", ".yaml", ".yml", ".txt", ".log", ".cfg", ".conf", ".png", ".jpg", ".jpeg", ".pdf"];

    public static RouteGroupBuilder MapSensorEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (ISensorRepository repo) =>
            Results.Ok(repo.GetAll().Select(ToSummary)));

        group.MapGet("/{id:guid}", (Guid id, ISensorRepository repo) =>
            repo.GetById(id) is { } sensor ? Results.Ok(ToSummary(sensor)) : Results.NotFound());

        group.MapPost("/", (RegisterSensorRequest request, ISensorRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(request.MacAddress))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["macAddress"] = ["MAC address is required."]
                });
            }

            if (repo.MacAddressExists(request.MacAddress))
            {
                return Results.Conflict(new { message = $"A sensor with MAC address '{request.MacAddress}' is already registered." });
            }

            var sensor = repo.Add(new SensorRegistration
            {
                MacAddress = request.MacAddress,
                FacilityName = request.FacilityName,
                ZoneName = request.ZoneName,
                SubZoneName = request.SubZoneName,
                NodeId = request.NodeId,
                Category = request.Category
            });

            return Results.Created($"/api/sensors/{sensor.Id}", ToSummary(sensor));
        });

        group.MapPost("/{id:guid}/attachments", async (Guid id, HttpRequest httpRequest, ISensorRepository repo, IWebHostEnvironment env) =>
        {
            if (repo.GetById(id) is null)
            {
                return Results.NotFound(new { message = "Sensor not found." });
            }

            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Expected multipart/form-data." });
            }

            var form = await httpRequest.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "No file was uploaded." });
            }

            if (file.Length > MaxAttachmentBytes)
            {
                return Results.BadRequest(new { message = "File exceeds the 25 MB attachment limit." });
            }

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(extension.ToLowerInvariant()))
            {
                return Results.BadRequest(new { message = $"File type '{extension}' is not permitted." });
            }

            var uploadsRoot = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
            Directory.CreateDirectory(uploadsRoot);

            // Never trust the client-supplied file name for the on-disk path -
            // store under a generated id and keep the original name only as metadata.
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var storagePath = Path.Combine(uploadsRoot, storedFileName);

            await using (var stream = File.Create(storagePath))
            {
                await file.CopyToAsync(stream);
            }

            var attachment = repo.AddAttachment(id, new SensorAttachment
            {
                SensorId = id,
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                StoragePath = storagePath
            });

            return attachment is null
                ? Results.NotFound()
                : Results.Created($"/api/sensors/{id}/attachments/{attachment.Id}", ToAttachmentDto(attachment));
        }).DisableAntiforgery();

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", (Guid id, Guid attachmentId, ISensorRepository repo) =>
        {
            var sensor = repo.GetById(id);
            var attachment = sensor?.Attachments.FirstOrDefault(a => a.Id == attachmentId);
            if (attachment is null || !File.Exists(attachment.StoragePath))
            {
                return Results.NotFound();
            }

            return Results.File(attachment.StoragePath, attachment.ContentType, attachment.FileName);
        });

        return group;
    }

    private static SensorSummaryDto ToSummary(SensorRegistration sensor) => new()
    {
        Id = sensor.Id,
        MacAddress = sensor.MacAddress,
        DeploymentPath = sensor.DeploymentPath,
        NodeId = sensor.NodeId,
        Category = sensor.Category,
        RegisteredAtUtc = sensor.RegisteredAtUtc,
        AttachmentCount = sensor.Attachments.Count
    };

    private static SensorAttachmentDto ToAttachmentDto(SensorAttachment attachment) => new()
    {
        Id = attachment.Id,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        SizeBytes = attachment.SizeBytes,
        UploadedAtUtc = attachment.UploadedAtUtc
    };
}
