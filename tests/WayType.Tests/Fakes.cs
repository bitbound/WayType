using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.History;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Prompts;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;
using WayType.Libraries.Core.Theming;

namespace WayType.Tests;

public sealed class InMemoryFileStore : IFileStore
{
    // Bytes rather than text, so recordings written through WriteBytesAsync read back intact.
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Paths => [.. _files.Keys];

    public List<string> RestrictedPaths { get; } = [];

    public bool FileExists(string path) => _files.ContainsKey(path);

    public string ReadAllText(string path) => System.Text.Encoding.UTF8.GetString(_files[path]);

    public string? ReadAllTextOrNull(string path) => _files.TryGetValue(path, out var contents) ? ReadAllText(path) : null;

    public byte[] ReadAllBytes(string path) => _files[path];

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(contents);

        return Task.CompletedTask;
    }

    public Task WriteBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        _files[path] = contents;

        return Task.CompletedTask;
    }

    public void DeleteFile(string path) => _files.Remove(path);

    public void EnsureDirectory(string path)
    {
    }

    public void RestrictToOwner(string path) => RestrictedPaths.Add(path);

    public string[] GetFileNames(string directory, string searchPattern) => [];

    public void ReplaceFile(string sourcePath, string destinationPath)
    {
        _files[destinationPath] = _files[sourcePath];
        _files.Remove(sourcePath);
    }
}

public sealed class TestPlatformPaths : IPlatformPaths
{
    public string ConfigDirectory => "/config/waytype";

    public string DataDirectory => "/data/waytype";

    public string SettingsFilePath => "/config/waytype/settings.json";

    public string PromptsFilePath => "/config/waytype/prompts.json";

    public string HistoryFilePath => "/data/waytype/history.json";

    public string AudioDirectory => "/data/waytype/audio";

    public string RemoteDesktopRestoreTokenPath => "/config/waytype/remotedesktop-restore-token";

    public string HotkeyRestoreTokenPath => "/config/waytype/hotkey-registration.json";

    public string UpdateStagingDirectory => "/tmp/waytype/update";

    public void EnsureDirectories()
    {
    }
}

public sealed class FakeTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => 1_000;

    public override long GetTimestamp() => _timestamp;

    public void Advance(long milliseconds) => _timestamp += milliseconds;
}

public sealed class RecordingFileRestoreTokenStore : IRestoreTokenStore
{
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public string? Read(string path) => _tokens.GetValueOrDefault(path);

    public void Save(string path, string token) => _tokens[path] = token;

    public void Delete(string path) => _tokens.Remove(path);
}

public sealed class FakeAudioRecorder : IAudioRecorder
{
    public PcmAudio Result { get; set; } = new([0.5f, -0.5f, 0.25f, -0.25f], 16_000, 1);

    public int CallCount { get; private set; }

    public string? LastDeviceId { get; private set; }

    public Task<PcmAudio> RecordAsync(string? deviceId, CancellationToken endOfRecording, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastDeviceId = deviceId;

        return Task.FromResult(Result);
    }
}

public sealed class FakeAudioPlayer : IAudioPlayer
{
    public List<byte[]> Played { get; } = [];

    public TaskCompletionSource? Gate { get; set; }

    public Task PlayAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        Played.Add(wavBytes);

        return Gate is null ? Task.CompletedTask : Gate.Task.WaitAsync(cancellationToken);
    }
}

public sealed class FakeSpeechToTextClient : ISpeechToTextClient
{    public string Text { get; set; } = "hello there";

    public List<byte[]> Received { get; } = [];

    public List<string?> ListEndpoints { get; } = [];

    public Exception? Throw { get; set; }

    public Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        Received.Add(wavBytes);

        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(Text);
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default)
    {
        ListEndpoints.Add(endpoint);

        return Task.FromResult<IReadOnlyList<AiModel>>([new AiModel("whisper-1")]);
    }
}

public sealed class FakeTextGenerationClient : ITextGenerationClient
{
    public string Text { get; set; } = "cleaned up";

    public List<string> Prompts { get; } = [];

    public List<string?> ListEndpoints { get; } = [];

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Prompts.Add(prompt);

        return Task.FromResult(Text);
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default)
    {
        ListEndpoints.Add(endpoint);

        return Task.FromResult<IReadOnlyList<AiModel>>([new AiModel("gpt-4o-mini")]);
    }
}

public sealed class FakeTextInputInjector : ITextInputInjector
{
    public List<string> Typed { get; } = [];

    public bool ProbeResult { get; set; } = true;

    public bool HasSavedGrant { get; set; }

    public Task<bool> ProbeGrantAsync(CancellationToken cancellationToken = default) => Task.FromResult(ProbeResult);

    public Task<bool> RequestGrantAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task RevokeGrantAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        Typed.Add(text);

        return Task.CompletedTask;
    }
}

public sealed class FakeSystemColorSchemeSource : ISystemColorSchemeSource
{
    public ColorSchemePreference Current { get; set; } = ColorSchemePreference.Unset;

    public event EventHandler<ColorSchemePreference>? Changed;

    public void Raise(ColorSchemePreference preference)
    {
        Current = preference;
        Changed?.Invoke(this, preference);
    }
}

/// <summary>
/// Holds the recording open until the coordinator signals the end of recording, which is what
/// StopAsync does. Without the gate the run can move past Listening before StopAsync is called.
/// </summary>
public sealed class GatedAudioRecorder : IAudioRecorder
{
    public PcmAudio Result { get; set; } = new([0.1f, 0.2f, 0.3f, 0.4f], 16_000, 1);

    public int CallCount { get; private set; }

    public string? LastDeviceId { get; private set; }

    public async Task<PcmAudio> RecordAsync(string? deviceId, CancellationToken endOfRecording, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastDeviceId = deviceId;

        try
        {
            await Task.Delay(Timeout.Infinite, endOfRecording).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return Result;
    }
}

public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<string> RequestUris { get; } = [];

    public List<string?> AuthorizationValues { get; } = [];

    public List<string> RequestBodies { get; } = [];

    /// <summary>
    /// Holds the response back so request timeouts can be exercised. The delay honours the token the
    /// client passes down, so a request timeout surfaces as cancellation rather than a hung test.
    /// </summary>
    public TimeSpan Delay { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
        AuthorizationValues.Add(request.Headers.Authorization?.ToString());

        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        return responder(request);
    }

    public static HttpResponseMessage Json(string payload, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

public sealed class FakeAppInfo(Version? version = null) : IAppInfo
{
    public string ProductName => "WayType";

    public string AppId => "io.github.testorg.waytype";

    public Version Version { get; } = version ?? new Version(1, 0, 0);

    public string RepositoryUrl => "https://github.com/TestOrg/WayType";

    public string RepositorySlug => "TestOrg/WayType";
}

internal static class TestSettings
{
    public static SettingsService Create(IFileStore fileStore, AppSettings? settings = null)
    {
        var paths = new TestPlatformPaths();

        if (settings is not null)
        {
            fileStore.WriteAllTextAsync(paths.SettingsFilePath,
                System.Text.Json.JsonSerializer.Serialize(settings, WayType.Libraries.Core.Serialization.WayTypeJson.Options))
                .GetAwaiter().GetResult();
        }

        return new SettingsService(paths, fileStore, Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsService>.Instance);
    }

    public static AppSettings ConfiguredSst()
    {
        return new AppSettings
        {
            SpeechToText = new SpeechToTextSettings { Endpoint = "https://api.example.test/v1", ModelId = "whisper-1" },
        };
    }
}
