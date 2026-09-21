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

    /// <summary>
    /// Model listing is a small request against the same host that serves inference, so it gets a
    /// shorter budget than generating text.
    /// </summary>
    private static readonly TimeSpan ModelListTimeout = TimeSpan.FromSeconds(30);

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
    /// <param name="timeout">
    /// Per-request budget. The clients are configured with no default timeout so this value is the
    /// only limit, which lets a slow reasoning model be given as long as the caller asks for.
    /// </param>
    public static async Task<string> SendAsync(HttpClient httpClient, HttpRequestMessage request, ILogger logger, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string body;

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var response = await httpClient.SendAsync(request, linked.Token).ConfigureAwait(false);

            body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

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
        catch (OperationCanceledException)
        {
            logger.LogError("The AI endpoint at {Url} did not answer within {Seconds}s.", request.RequestUri, timeout.TotalSeconds);
            throw new AiEndpointException(
                $"The AI endpoint at {request.RequestUri} did not answer within {timeout.TotalSeconds:0.##}s.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
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

        var body = await SendAsync(httpClient, request, logger, ModelListTimeout, cancellationToken).ConfigureAwait(false);

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
