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
        await EnsureSuccessAsync(response);
    }

    public async Task<TelemetryEventDto?> IngestTelemetryAsync(TelemetryIngestRequest request)
    {
        var response = await http.PostAsJsonAsync("api/telemetry", request, JsonOptions);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<TelemetryEventDto>(JsonOptions);
    }

    public async Task<List<TelemetryEventDto>> SeedTelemetryAsync(int count)
    {
        var response = await http.PostAsJsonAsync("api/telemetry/seed", new SeedTelemetryRequest { Count = count }, JsonOptions);
        await EnsureSuccessAsync(response);
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
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<DeploymentValidationResponse>(JsonOptions);
        return (result?.IsValid ?? false, result?.Issues ?? []);
    }

    /// <summary>
    /// Turns any exception from an API call into a short message a page can
    /// show, so a stopped API or a rejected request never crashes the UI.
    /// </summary>
    public static string DescribeError(Exception ex) => ex switch
    {
        ApiRequestException api => api.Message,
        HttpRequestException => "Could not reach the Smart-X API. Check that it is running on the URL in wwwroot/appsettings.json.",
        TaskCanceledException => "The API took too long to respond.",
        JsonException => "The API returned data in an unexpected format.",
        _ => ex.Message
    };

    // A non-success response is turned into an exception carrying the API's
    // own message (e.g. "Unknown device") rather than the response body being
    // parsed as if it were a success payload.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var message = TryReadMessage(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
        throw new ApiRequestException(message, (int)response.StatusCode);
    }

    private static string? TryReadMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "message", "detail", "title" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through and show the raw text.
        }

        return body.Length > 200 ? body[..200] : body;
    }

    private sealed class DeploymentValidationResponse
    {
        public bool IsValid { get; set; }
        public List<ValidationIssue> Issues { get; set; } = [];
    }
}
