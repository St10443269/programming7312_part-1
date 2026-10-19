using System.Text.Json;

namespace SmartX.Api.Endpoints;

/// <summary>
/// Reads a JSON request body without letting malformed JSON turn into an
/// exception page: the caller gets either the parsed body or a short error
/// message to return as a 400.
/// </summary>
public static class RequestBody
{
    public static async Task<(T? Body, string? Error)> TryReadAsync<T>(HttpRequest request) where T : class
    {
        if (!request.HasJsonContentType())
        {
            return (null, "Expected a JSON body (Content-Type: application/json).");
        }

        try
        {
            var body = await request.ReadFromJsonAsync<T>();
            return body is null ? (null, "Request body is empty.") : (body, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Malformed JSON: {ex.Message}");
        }
    }
}
