using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;

namespace WayType.Libraries.Speech;

/// <summary>
/// Sends recorded audio to an OpenAI-compatible /audio/transcriptions endpoint.
/// </summary>
public sealed class OpenAiSpeechToTextClient(HttpClient httpClient, ISettingsService settings, ILogger<OpenAiSpeechToTextClient> logger) : ISpeechToTextClient
{
    /// <summary>
    /// Uploading a recording and waiting for a transcript. Generous, because a long take against a
    /// busy local model can take a while.
    /// </summary>
    private static readonly TimeSpan TranscriptionTimeout = TimeSpan.FromMinutes(5);

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        var speech = settings.Current.SpeechToText;

        if (speech.ModelSelectionEnabled && string.IsNullOrWhiteSpace(speech.ModelId))
        {
            throw new AiEndpointException("Set a speech-to-text model before transcribing.");
        }

        // A server that cannot list models does not accept a model choice either, so the field is
        // left out entirely even when a stale id is still saved.
        var model = speech.ModelSelectionEnabled ? speech.ModelId : null;

        using var request = new HttpRequestMessage(HttpMethod.Post, AiEndpoint.BuildUri(speech.Endpoint, "/audio/transcriptions"));
        request.Content = BuildForm(model, speech.Language, wavBytes);
        AiEndpointRequests.ApplyAuthorization(request, speech.ApiKey);

        var body = await AiEndpointRequests.SendAsync(httpClient, request, logger, TranscriptionTimeout, cancellationToken).ConfigureAwait(false);

        using var document = AiEndpointRequests.ParseResponse(body);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new AiEndpointException("The transcription response was not a JSON object.", detail: AiEndpointRequests.Truncate(body));
        }

        var text = document.RootElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()
            : null;

        return (text ?? string.Empty).Trim();
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default)
    {
        return AiEndpointRequests.ListModelsAsync(httpClient, endpoint, apiKey, logger, cancellationToken);
    }

    private static MultipartFormDataContent BuildForm(string? model, string? language, byte[] wavBytes)
    {
        var form = new MultipartFormDataContent();

        var audio = new ByteArrayContent(wavBytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "audio.wav");

        if (!string.IsNullOrWhiteSpace(model))
        {
            form.Add(new StringContent(model), "model");
        }

        form.Add(new StringContent("json"), "response_format");

        if (!string.IsNullOrWhiteSpace(language))
        {
            form.Add(new StringContent(language), "language");
        }

        return form;
    }
}
