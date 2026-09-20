using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Speech;

namespace WayType.Libraries.Speech;

/// <summary>
/// Shared request plumbing for the OpenAI-compatible clients: auth headers, response checks,
/// and model listing.
/// </summary>
internal static class AiEndpointRequests
{
    private const int MaxDetailLength = 2000;

    public static void ApplyAuthorization(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>
    /// Sends the request and returns the response body. Any non-2xx status, timeout, or connection
    /// failure is surfaced as <see cref="AiEndpointException"/>. Caller cancellation propagates.
    /// </summary>
    public static async Task<string> SendAsync(HttpClient httpClient, HttpRequestMessage request, ILogger logger, CancellationToken cancellationToken)
    {
        string body;

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new AiEndpointException(
                    $"The AI endpoint at {request.RequestUri} returned status {(int)response.StatusCode}.",
                    statusCode: (int)response.StatusCode,
                    detail: Truncate(body));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            logger.LogError(ex, "Failed to reach the AI endpoint at {Url}.", request.RequestUri);
            throw new AiEndpointException($"Could not reach the AI endpoint at {request.RequestUri}.", detail: ex.Message);
        }

        return body;
    }

    public static async Task<IReadOnlyList<AiModel>> ListModelsAsync(HttpClient httpClient, string? endpoint, string? apiKey, ILogger logger, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AiEndpoint.BuildUri(endpoint, "/models"));
        ApplyAuthorization(request, apiKey);

        var body = await SendAsync(httpClient, request, logger, cancellationToken).ConfigureAwait(false);

        using var document = ParseResponse(body);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<AiModel>();

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var idValue = id.GetString();

            if (string.IsNullOrEmpty(idValue))
            {
                continue;
            }

            var ownedBy = item.TryGetProperty("owned_by", out var owner) && owner.ValueKind == JsonValueKind.String
                ? owner.GetString()
                : null;

            models.Add(new AiModel(idValue, ownedBy));
        }

        return models;
    }

    public static JsonDocument ParseResponse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new AiEndpointException("The endpoint returned a response that is not valid JSON.", detail: Truncate(body));
        }
    }

    public static string Truncate(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= MaxDetailLength)
        {
            return value;
        }

        return value[..MaxDetailLength] + "...";
    }
}
