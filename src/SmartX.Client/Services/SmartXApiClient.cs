using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Shared.Dto;
using SmartX.Shared.Models;
using SmartX.Shared.Validation;

namespace SmartX.Client.Services;

/// <summary>Thin typed wrapper around the Smart-X ingestion API so pages don't build requests by hand.</summary>
public sealed class SmartXApiClient(HttpClient http)
{
    // Must mirror the API's ConfigureHttpJsonOptions setup (Program.cs) - the
    // server serializes enums as strings ("Environmental"), so the client's
    // own (de)serialization needs the same JsonStringEnumConverter or every
    // enum-bearing response fails to parse.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<SensorSummaryDto>> GetSensorsAsync() =>
        await http.GetFromJsonAsync<List<SensorSummaryDto>>("api/sensors", JsonOptions) ?? [];

    public async Task<(bool Success, string? Error)> RegisterSensorAsync(RegisterSensorRequest request)
    {
        var response = await http.PostAsJsonAsync("api/sensors", request, JsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return (true, null);
        }

        var body = await response.Content.ReadAsStringAsync();
        return (false, string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);
    }

    public async Task UploadAttachmentAsync(Guid sensorId, Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);

        var response = await http.PostAsync($"api/sensors/{sensorId}/attachments", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TelemetryEventDto?> IngestTelemetryAsync(TelemetryIngestRequest request) =>
        await (await http.PostAsJsonAsync("api/telemetry", request, JsonOptions)).Content.ReadFromJsonAsync<TelemetryEventDto>(JsonOptions);

    public async Task<List<TelemetryEventDto>> SeedTelemetryAsync(int count)
    {
        var response = await http.PostAsJsonAsync("api/telemetry/seed", new SeedTelemetryRequest { Count = count }, JsonOptions);
        return await response.Content.ReadFromJsonAsync<List<TelemetryEventDto>>(JsonOptions) ?? [];
    }

    public async Task<List<TelemetryEventDto>> GetFeedAsync(int take = 30) =>
        await http.GetFromJsonAsync<List<TelemetryEventDto>>($"api/telemetry/feed?take={take}", JsonOptions) ?? [];

    public async Task<GridSnapshotDto?> GetGridSnapshotAsync() =>
        await http.GetFromJsonAsync<GridSnapshotDto>("api/telemetry/grid", JsonOptions);

    public async Task<AggregateResultDto?> GetAggregateAsync(string nodeId) =>
        await http.GetFromJsonAsync<AggregateResultDto>($"api/telemetry/aggregate/{Uri.EscapeDataString(nodeId)}", JsonOptions);

    public async Task<DeploymentNode?> GetSampleDeploymentTreeAsync() =>
        await http.GetFromJsonAsync<DeploymentNode>("api/deployment/sample", JsonOptions);

    public async Task<(bool IsValid, List<ValidationIssue> Issues)> ValidateDeploymentTreeAsync(DeploymentNode tree)
    {
        var response = await http.PostAsJsonAsync("api/deployment/validate", tree, JsonOptions);
        var result = await response.Content.ReadFromJsonAsync<DeploymentValidationResponse>(JsonOptions);
        return (result?.IsValid ?? false, result?.Issues ?? []);
    }

    private sealed class DeploymentValidationResponse
    {
        public bool IsValid { get; set; }
        public List<ValidationIssue> Issues { get; set; } = [];
    }
}
