namespace SmartX.Client.Services;

/// <summary>The API was reachable but rejected the request (4xx/5xx); <see cref="Exception.Message"/> holds the API's own explanation.</summary>
public sealed class ApiRequestException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
