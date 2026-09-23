using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.Dictation;
using WayType.Libraries.Core.History;
using WayType.Libraries.Core.Prompts;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;

namespace WayType.Tests;

public class DictationCoordinatorTests
{
    private readonly InMemoryFileStore _fileStore = new();
    private readonly GatedAudioRecorder _recorder = new();
    private readonly RecordingStore _recordings;
    private readonly FakeSpeechToTextClient _speechToText = new();
    private readonly FakeTextGenerationClient _textGeneration = new();
    private readonly FakeTextInputInjector _injector = new();
    private readonly SettingsService _settings;
    private readonly HistoryService _history;
    private readonly DictationCoordinator _coordinator;

    public DictationCoordinatorTests()
    {
        _settings = TestSettings.Create(_fileStore, TestSettings.ConfiguredSst());
        _recordings = new RecordingStore(new TestPlatformPaths(), _fileStore);
        _history = new HistoryService(new TestPlatformPaths(), _fileStore, _recordings, _settings);
        _coordinator = Create(settings: _settings);
    }

    [Fact]
    public async Task StartAsync_WhenSpeechToTextIsNotConfigured_SetsErrorState()
    {
        var coordinator = Create(TestSettings.Create(new InMemoryFileStore()));
        var ct = TestContext.Current.CancellationToken;

        await coordinator.StartAsync(ct);

        Assert.Equal(DictationState.Error, coordinator.State);
        Assert.Contains("Settings", coordinator.LastError);
        Assert.Equal(0, _recorder.CallCount);
    }

    [Fact]
    public async Task StartAsync_WhenModelSelectionIsDisabledAndOnlyTheEndpointIsSet_StartsListening()
    {
        var appSettings = TestSettings.ConfiguredSst();
        appSettings.SpeechToText.ModelId = null;
        appSettings.SpeechToText.ModelSelectionEnabled = false;

        var coordinator = Create(TestSettings.Create(new InMemoryFileStore(), appSettings));
        var ct = TestContext.Current.CancellationToken;

        await coordinator.StartAsync(ct);
        Assert.True(coordinator.IsListening);

        await coordinator.StopAsync(ct);
    }

    [Fact]
    public async Task StopAsync_WithoutPostProcessing_TypesTranscriptionAndRecordsHistory()
    {
        var ct = TestContext.Current.CancellationToken;

        await _coordinator.StartAsync(ct);
        Assert.True(_coordinator.IsListening);

        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Idle, _coordinator.State);
        Assert.Equal(["hello there"], _injector.Typed);
        Assert.Single(_speechToText.Received);

        var entry = Assert.Single(_history.GetAll());
        Assert.Equal("hello there", entry.Text);
        Assert.Null(entry.Transcription);
        Assert.Null(entry.PromptTitle);
        Assert.Equal("whisper-1", entry.ModelId);
    }

    [Fact]
    public async Task StopAsync_WithPostProcessing_TypesModelOutputAndKeepsTheRawTranscription()
    {
        var ct = TestContext.Current.CancellationToken;

        _settings.Current.PostProcessing.Enabled = true;
        _settings.Current.PostProcessing.Endpoint = "https://text.example.test/v1";
        _settings.Current.PostProcessing.ModelId = "qwen3";

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(["cleaned up"], _injector.Typed);
        Assert.Contains("hello there", Assert.Single(_textGeneration.Prompts));

        var entry = Assert.Single(_history.GetAll());
        Assert.Equal("cleaned up", entry.Text);
        Assert.Equal("hello there", entry.Transcription);
        Assert.NotNull(entry.PromptTitle);
    }

    [Fact]
    public async Task StopAsync_WhenPostProcessingFails_KeepsTheRawTranscriptionInHistory()
    {
        var ct = TestContext.Current.CancellationToken;

        _settings.Current.PostProcessing.Enabled = true;
        _settings.Current.PostProcessing.Endpoint = "https://text.example.test/v1";
        _settings.Current.PostProcessing.ModelId = "qwen3";
        _textGeneration.Throw = new AiEndpointException("The endpoint timed out.", statusCode: null);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Error, _coordinator.State);
        Assert.Empty(_injector.Typed);

        var entry = Assert.Single(_history.GetAll());
        Assert.Equal("hello there", entry.Text);
        Assert.Null(entry.Transcription);
        Assert.Null(entry.PromptTitle);
    }

    [Fact]
    public async Task StopAsync_WhenTranscriptionFails_SetsErrorStateAndTypesNothing()
    {
        var ct = TestContext.Current.CancellationToken;

        _speechToText.Throw = new AiEndpointException("The endpoint rejected the audio.", statusCode: 400);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Error, _coordinator.State);
        Assert.Equal("The endpoint rejected the audio.", _coordinator.LastError);
        Assert.Empty(_injector.Typed);
        Assert.Empty(_history.GetAll());
    }

    [Fact]
    public async Task StopAsync_WhenNothingWasCaptured_ReturnsToIdleWithoutTranscribing()
    {
        var ct = TestContext.Current.CancellationToken;

        _recorder.Result = PcmAudio.Empty;

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Idle, _coordinator.State);
        Assert.Empty(_speechToText.Received);
        Assert.Empty(_injector.Typed);
    }

    [Fact]
    public async Task StopAsync_WhenCaptureIsSilent_ReturnsToIdleWithoutTranscribing()
    {
        var ct = TestContext.Current.CancellationToken;

        _recorder.Result = new PcmAudio([0.001f, -0.001f, 0.0005f, -0.0005f], 16_000, 1);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Idle, _coordinator.State);
        Assert.Empty(_speechToText.Received);
        Assert.Empty(_injector.Typed);
    }

    [Fact]
    public async Task StopAsync_WhenCaptureHasOnlyAnIsolatedSpike_ReturnsToIdleWithoutTranscribing()
    {
        var ct = TestContext.Current.CancellationToken;

        _recorder.Result = new PcmAudio([0.2f, .. Enumerable.Repeat(0f, 999)], 16_000, 1);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Idle, _coordinator.State);
        Assert.Empty(_speechToText.Received);
        Assert.Empty(_injector.Typed);
    }

    [Fact]
    public async Task StopAsync_WhenCaptureIsQuietSpeech_UsesTheConfiguredThreshold()
    {
        var ct = TestContext.Current.CancellationToken;

        _settings.Current.SilenceRmsThreshold = 0.001f;
        _recorder.Result = new PcmAudio(Enumerable.Repeat(0.002f, 1_000).ToArray(), 16_000, 1);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Idle, _coordinator.State);
        Assert.Single(_speechToText.Received);
        Assert.Equal(["hello there"], _injector.Typed);
    }

    [Fact]
    public async Task StopAsync_PassesTheSelectedInputDeviceToTheRecorder()
    {
        var ct = TestContext.Current.CancellationToken;

        _settings.Current.InputDeviceId = "alsa_input.usb-mic";

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal("alsa_input.usb-mic", _recorder.LastDeviceId);
    }

    [Fact]
    public async Task ToggleAsync_AfterAFailedRun_StartsANewCapture()
    {
        var ct = TestContext.Current.CancellationToken;

        _speechToText.Throw = new AiEndpointException("The endpoint rejected the audio.", statusCode: 404);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Error, _coordinator.State);

        _speechToText.Throw = null;

        await _coordinator.ToggleAsync(ct);

        Assert.True(_coordinator.IsListening);

        await _coordinator.StopAsync(ct);

        Assert.Equal(2, _recorder.CallCount);
        Assert.Equal(["hello there"], _injector.Typed);
    }

    [Fact]
    public async Task StartAsync_AfterAFailedRun_ListensAgainAndDropsTheStaleError()
    {
        var ct = TestContext.Current.CancellationToken;

        _speechToText.Throw = new AiEndpointException("The endpoint rejected the audio.", statusCode: 404);

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Equal(DictationState.Error, _coordinator.State);
        Assert.NotNull(_coordinator.LastError);

        _speechToText.Throw = null;

        await _coordinator.StartAsync(ct);

        Assert.True(_coordinator.IsListening);
        Assert.Null(_coordinator.LastError);
    }

    [Fact]
    public async Task StopAsync_WithRecordingsEnabled_KeepsTheAudioOnDisk()
    {
        var ct = TestContext.Current.CancellationToken;

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        var entry = Assert.Single(_history.GetAll());

        Assert.NotNull(entry.AudioFileName);
        Assert.True(_recordings.Exists(entry.AudioFileName));
        Assert.Equal(_speechToText.Received[0], _recordings.Read(entry.AudioFileName));
    }

    [Fact]
    public async Task StopAsync_WithRecordingsDisabled_KeepsNoAudio()
    {
        var ct = TestContext.Current.CancellationToken;

        _settings.Current.KeepRecordings = false;

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        var entry = Assert.Single(_history.GetAll());

        Assert.Null(entry.AudioFileName);
        AssertNoAudioFiles();
    }

    [Fact]
    public async Task StopAsync_WhenTheTranscriptionIsEmpty_LeavesNoRecordingBehind()
    {
        var ct = TestContext.Current.CancellationToken;

        _speechToText.Text = "   ";

        await _coordinator.StartAsync(ct);
        await _coordinator.StopAsync(ct);

        Assert.Empty(_history.GetAll());
        AssertNoAudioFiles();
    }

    private void AssertNoAudioFiles()
    {
        Assert.DoesNotContain(_fileStore.Paths, path => path.StartsWith(new TestPlatformPaths().AudioDirectory, StringComparison.Ordinal));
    }

    private DictationCoordinator Create(SettingsService settings)
    {
        var prompts = new PromptService(new TestPlatformPaths(), _fileStore, settings);

        return new DictationCoordinator(
            _recorder,
            _recordings,
            _speechToText,
            _textGeneration,
            prompts,
            _history,
            _injector,
            settings,
            new FakeTimeProvider(),
            NullLogger<DictationCoordinator>.Instance);
    }
}
