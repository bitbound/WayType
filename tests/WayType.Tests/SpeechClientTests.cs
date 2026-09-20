using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;
using WayType.Libraries.Speech;

namespace WayType.Tests;

public class OpenAiSpeechToTextClientTests
{
    [Fact]
    public async Task TranscribeAsync_WhenEndpointHasNoVersionSegment_PostsUnderV1()
    {
        var (client, handler) = Create(Speech(endpoint: "https://api.example.test/"), _ => StubHttpMessageHandler.Json("""{"text":"ok"}"""));

        await client.TranscribeAsync([1, 2, 3], TestContext.Current.CancellationToken);

        Assert.Equal("https://api.example.test/v1/audio/transcriptions", handler.RequestUris[0]);
        Assert.Equal("Bearer secret-key", handler.AuthorizationValues[0]);
    }

    [Fact]
    public async Task TranscribeAsync_SendsTheAudioFileModelAndJsonResponseFormat()
    {
        var (client, handler) = Create(Speech(), _ => StubHttpMessageHandler.Json("""{"text":"ok"}"""));

        await client.TranscribeAsync([1, 2, 3], TestContext.Current.CancellationToken);

        var body = handler.RequestBodies[0];

        Assert.Contains("name=file", body);
        Assert.Contains("filename=audio.wav", body);
        Assert.Contains("name=model", body);
        Assert.Contains("whisper-1", body);
        Assert.Contains("name=response_format", body);
        Assert.DoesNotContain("name=language", body);
    }

    [Fact]
    public async Task TranscribeAsync_WhenLanguageIsSet_SendsTheLanguageField()
    {
        var settings = Speech();
        settings.SpeechToText.Language = "en-US";

        var (client, handler) = Create(settings, _ => StubHttpMessageHandler.Json("""{"text":"ok"}"""));

        await client.TranscribeAsync([1], TestContext.Current.CancellationToken);

        Assert.Contains("name=language", handler.RequestBodies[0]);
        Assert.Contains("en-US", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsTheTrimmedTranscription()
    {
        var (client, _) = Create(Speech(), _ => StubHttpMessageHandler.Json("""{"text":"  hello  there "}"""));

        var text = await client.TranscribeAsync([1], TestContext.Current.CancellationToken);

        Assert.Equal("hello  there", text);
    }

    [Fact]
    public async Task TranscribeAsync_WhenEndpointReturnsAnError_ThrowsWithStatusAndDetail()
    {
        var (client, _) = Create(Speech(), _ => StubHttpMessageHandler.Json("""{"error":"bad model"}""", HttpStatusCode.BadRequest));

        var exception = await Assert.ThrowsAsync<AiEndpointException>(
            () => client.TranscribeAsync([1], TestContext.Current.CancellationToken));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("bad model", exception.Detail);
    }

    [Fact]
    public async Task TranscribeAsync_WithoutAModel_ThrowsBeforeSending()
    {
        var settings = Speech();
        settings.SpeechToText.ModelId = null;

        var (client, handler) = Create(settings, _ => StubHttpMessageHandler.Json("{}"));

        await Assert.ThrowsAsync<AiEndpointException>(
            () => client.TranscribeAsync([1], TestContext.Current.CancellationToken));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task ListModelsAsync_WhenEndpointAlreadyCarriesV1_RequestsModelsOnce()
    {
        var (client, handler) = Create(Speech(endpoint: "https://api.example.test/v1/"), _ => StubHttpMessageHandler.Json(
            """{"data":[{"id":"whisper-1","owned_by":"openai"},{"id":"groq-whisper","owned_by":"groq"}]}"""));

        var models = await client.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://api.example.test/v1/models", handler.RequestUris[0]);
        Assert.Equal(["whisper-1", "groq-whisper"], models.Select(model => model.Id));
        Assert.Equal("groq", models[1].OwnedBy);
    }

    private static AppSettings Speech(string endpoint = "https://api.example.test")
    {
        return new AppSettings
        {
            SpeechToText = new SpeechToTextSettings
            {
                Endpoint = endpoint,
                ModelId = "whisper-1",
                ApiKey = "secret-key",
            },
        };
    }

    private static (OpenAiSpeechToTextClient Client, StubHttpMessageHandler Handler) Create(
        AppSettings settings,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var client = new OpenAiSpeechToTextClient(
            new HttpClient(handler),
            TestSettings.Create(new InMemoryFileStore(), settings),
            NullLogger<OpenAiSpeechToTextClient>.Instance);

        return (client, handler);
    }
}

public class OpenAiTextGenerationClientTests
{
    [Fact]
    public async Task CompleteAsync_SendsThePromptToChatCompletionsWithTheBearerKey()
    {
        var (client, handler) = Create(Post(endpoint: "https://text.example.test"), _ => StubHttpMessageHandler.Json(Choice("cleaned")));

        var text = await client.CompleteAsync("fix this", TestContext.Current.CancellationToken);

        Assert.Equal("https://text.example.test/v1/chat/completions", handler.RequestUris[0]);
        Assert.Equal("Bearer text-key", handler.AuthorizationValues[0]);
        Assert.Equal("cleaned", text);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();

        Assert.Equal("qwen3", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("fix this", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_WithUnsetOptions_OmitsThemFromTheBody()
    {
        var (client, handler) = Create(Post(), _ => StubHttpMessageHandler.Json(Choice("x")));

        await client.CompleteAsync("prompt", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);

        foreach (var name in new[] { "temperature", "top_p", "max_tokens", "max_completion_tokens", "reasoning_effort", "chat_template_kwargs", "stop", "response_format" })
        {
            Assert.False(body.RootElement.TryGetProperty(name, out _), $"{name} should be absent");
        }
    }

    [Fact]
    public async Task CompleteAsync_WithThinkingDisabled_SendsTheChatTemplateSwitchAsABoolean()
    {
        var settings = Post();
        settings.PostProcessing.Options.ThinkingEnabled = false;

        var (client, handler) = Create(settings, _ => StubHttpMessageHandler.Json(Choice("x")));

        await client.CompleteAsync("prompt", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);

        Assert.False(body.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public async Task CompleteAsync_WhenAdvancedOverridesAreSet_TheyWinOverTheTypedOptions()
    {
        var settings = Post();
        settings.PostProcessing.Options.Temperature = 0.2;
        settings.PostProcessing.Options.AdvancedOverridesJson = """{"temperature":0.9,"chat_template_kwargs":{"enable_thinking":false}}""";

        var (client, handler) = Create(settings, _ => StubHttpMessageHandler.Json(Choice("x")));

        await client.CompleteAsync("prompt", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);

        Assert.Equal(0.9, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.False(body.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public async Task CompleteAsync_WithMalformedAdvancedOverrides_Throws()
    {
        var settings = Post();
        settings.PostProcessing.Options.AdvancedOverridesJson = "not json";

        var (client, _) = Create(settings, _ => StubHttpMessageHandler.Json(Choice("x")));

        var exception = await Assert.ThrowsAsync<AiEndpointException>(
            () => client.CompleteAsync("prompt", TestContext.Current.CancellationToken));

        Assert.Contains("AdvancedOverridesJson", exception.Message);
    }

    [Fact]
    public async Task CompleteAsync_WhenTheModelReturnsNoChoices_Throws()
    {
        var (client, _) = Create(Post(), _ => StubHttpMessageHandler.Json("""{"choices":[]}"""));

        await Assert.ThrowsAsync<AiEndpointException>(
            () => client.CompleteAsync("prompt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenTheResponseIsNotJson_Throws()
    {
        var (client, _) = Create(Post(), _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>gateway</html>"),
        });

        var exception = await Assert.ThrowsAsync<AiEndpointException>(
            () => client.CompleteAsync("prompt", TestContext.Current.CancellationToken));

        Assert.Contains("not valid JSON", exception.Message);
    }

    private static string Choice(string content)
    {
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"" + content + "\"}}]}";
    }

    private static AppSettings Post(string endpoint = "https://text.example.test")
    {
        return new AppSettings
        {
            PostProcessing = new PostProcessingSettings
            {
                Enabled = true,
                Endpoint = endpoint,
                ModelId = "qwen3",
                ApiKey = "text-key",
            },
        };
    }

    private static (OpenAiTextGenerationClient Client, StubHttpMessageHandler Handler) Create(
        AppSettings settings,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var client = new OpenAiTextGenerationClient(
            new HttpClient(handler),
            TestSettings.Create(new InMemoryFileStore(), settings),
            NullLogger<OpenAiTextGenerationClient>.Instance);

        return (client, handler);
    }
}
