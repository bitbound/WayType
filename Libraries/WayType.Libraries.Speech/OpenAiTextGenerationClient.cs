using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;

namespace WayType.Libraries.Speech;

/// <summary>
/// Calls an OpenAI-compatible /chat/completions endpoint to clean up transcribed text.
/// </summary>
public sealed class OpenAiTextGenerationClient(HttpClient httpClient, ISettingsService settings, ILogger<OpenAiTextGenerationClient> logger) : ITextGenerationClient
{
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var post = settings.Current.PostProcessing;
        var model = post.ModelId;

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiEndpointException("Set a post-processing model before generating text.");
        }

        var payload = BuildRequestBody(model, prompt, post.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, AiEndpoint.BuildUri(post.Endpoint, "/chat/completions"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        AiEndpointRequests.ApplyAuthorization(request, post.ApiKey);

        var body = await AiEndpointRequests.SendAsync(httpClient, request, logger, post.Timeout, cancellationToken).ConfigureAwait(false);

        return ReadCompletion(body);
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default)
    {
        return AiEndpointRequests.ListModelsAsync(httpClient, endpoint, apiKey, logger, cancellationToken);
    }

    // The ThinkingEnabled boolean is the vLLM/Qwen chat-template switch (chat_template_kwargs.enable_thinking),
    // the only vendor shape here that is itself a bool. OpenAI's toggle is the string reasoning_effort and
    // Anthropic's is a thinking object with a token budget, so both are reached through AdvancedOverridesJson.
    private static string BuildRequestBody(string model, string prompt, TextGenerationOptions options)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt,
                },
            },
        };

        if (options.ThinkingEnabled.HasValue)
        {
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = options.ThinkingEnabled.Value };
        }

        if (!string.IsNullOrWhiteSpace(options.ReasoningEffort))
        {
            body["reasoning_effort"] = options.ReasoningEffort;
        }

        if (options.Temperature.HasValue)
        {
            body["temperature"] = options.Temperature.Value;
        }

        if (options.TopP.HasValue)
        {
            body["top_p"] = options.TopP.Value;
        }

        if (options.MaxCompletionTokens.HasValue)
        {
            body["max_completion_tokens"] = options.MaxCompletionTokens.Value;
        }

        if (options.MaxTokens.HasValue)
        {
            body["max_tokens"] = options.MaxTokens.Value;
        }

        if (options.FrequencyPenalty.HasValue)
        {
            body["frequency_penalty"] = options.FrequencyPenalty.Value;
        }

        if (options.PresencePenalty.HasValue)
        {
            body["presence_penalty"] = options.PresencePenalty.Value;
        }

        if (options.RepetitionPenalty.HasValue)
        {
            body["repetition_penalty"] = options.RepetitionPenalty.Value;
        }

        if (options.Seed.HasValue)
        {
            body["seed"] = options.Seed.Value;
        }

        if (options.N.HasValue)
        {
            body["n"] = options.N.Value;
        }

        if (!string.IsNullOrWhiteSpace(options.Stop))
        {
            body["stop"] = new JsonArray { options.Stop };
        }

        if (!string.IsNullOrWhiteSpace(options.ResponseFormat))
        {
            body["response_format"] = new JsonObject { ["type"] = options.ResponseFormat };
        }

        ApplyAdvancedOverrides(body, options.AdvancedOverridesJson);

        return body.ToJsonString();
    }

    // Merged last so a user's raw overrides win over every field the client set.
    private static void ApplyAdvancedOverrides(JsonObject body, string? advancedOverridesJson)
    {
        if (string.IsNullOrWhiteSpace(advancedOverridesJson))
        {
            return;
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(advancedOverridesJson);
        }
        catch (JsonException ex)
        {
            throw new AiEndpointException("The post-processing advanced overrides (AdvancedOverridesJson) are not valid JSON.", detail: ex.Message);
        }

        if (parsed is not JsonObject overrides)
        {
            throw new AiEndpointException("The post-processing advanced overrides (AdvancedOverridesJson) must be a JSON object.");
        }

        foreach (var property in overrides)
        {
            body[property.Key] = property.Value?.DeepClone();
        }
    }

    private static string ReadCompletion(string body)
    {
        using var document = AiEndpointRequests.ParseResponse(body);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new AiEndpointException("The completion response was not a JSON object.", detail: AiEndpointRequests.Truncate(body));
        }

        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new AiEndpointException("The model returned no completion choices.", detail: AiEndpointRequests.Truncate(body));
        }

        var choice = choices.EnumerateArray().First();

        if (choice.ValueKind == JsonValueKind.Object
            && choice.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return (content.GetString() ?? string.Empty).Trim();
        }

        throw new AiEndpointException("The model returned a completion with no text content.", detail: AiEndpointRequests.Truncate(body));
    }
}
