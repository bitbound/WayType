using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.History;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Prompts;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;

namespace WayType.Libraries.Core.Dictation;

/// <summary>
/// Runs one dictation end to end: capture, transcribe, optional post-process, history, then typing.
/// </summary>
public sealed class DictationCoordinator : IDictationCoordinator
{
    private readonly ITextInputInjector _injector;
    private readonly ILogger<DictationCoordinator> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly IAudioRecorder _recorder;
    private readonly ISettingsService _settings;
    private readonly IPromptService _prompts;
    private readonly IHistoryService _history;
    private readonly ISpeechToTextClient _speechToText;
    private readonly ITextGenerationClient _textGeneration;

    private CancellationTokenSource? _endOfRecording;
    private Task? _activeRun;

    public DictationCoordinator(
        IAudioRecorder recorder,
        ISpeechToTextClient speechToText,
        ITextGenerationClient textGeneration,
        IPromptService prompts,
        IHistoryService history,
        ITextInputInjector injector,
        ISettingsService settings,
        TimeProvider timeProvider,
        ILogger<DictationCoordinator> logger)
    {
        _recorder = recorder;
        _speechToText = speechToText;
        _textGeneration = textGeneration;
        _prompts = prompts;
        _history = history;
        _injector = injector;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public string? LastError { get; private set; }

    public bool IsListening => State == DictationState.Listening;

    public event EventHandler? StateChanged;

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening)
        {
            await StopAsync(cancellationToken);
            return;
        }

        if (State == DictationState.Idle)
        {
            await StartAsync(cancellationToken);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (State != DictationState.Idle)
            {
                return Task.CompletedTask;
            }

            if (!_settings.Current.SpeechToText.IsConfigured)
            {
                Fail("Configure the speech-to-text endpoint and model in Settings first.");
                return Task.CompletedTask;
            }

            _endOfRecording = new CancellationTokenSource();
            SetState(DictationState.Listening);

            var endOfRecording = _endOfRecording;
            var maxDuration = TimeSpan.FromSeconds(Math.Clamp(_settings.Current.MaximumRecordingSeconds, 1, 3600));

            _activeRun = Task.Run(() => RunAsync(endOfRecording, maxDuration, cancellationToken));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? run;

        lock (_gate)
        {
            if (State != DictationState.Listening)
            {
                return;
            }

            run = _activeRun;
        }

        _endOfRecording?.Cancel();

        if (run is not null)
        {
            await run.ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CancellationTokenSource endOfRecording, TimeSpan maxDuration, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var pcm = PcmAudio.Empty;

        try
        {
            using (endOfRecording)
            {
                endOfRecording.CancelAfter(maxDuration);

                pcm = await _recorder
                    .RecordAsync(_settings.Current.InputDeviceId, endOfRecording.Token, cancellationToken)
                    .ConfigureAwait(false);
            }

            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            if (pcm.FrameCount == 0)
            {
                SetState(DictationState.Idle);
                return;
            }

            SetState(DictationState.Transcribing);

            var wav = PcmProcessor.ToWav(pcm);
            var transcription = await _speechToText.TranscribeAsync(wav, cancellationToken).ConfigureAwait(false);
            var finalText = transcription.Trim();
            var promptTitle = (string?)null;

            if (_settings.Current.PostProcessing.IsConfigured && !string.IsNullOrWhiteSpace(finalText))
            {
                SetState(DictationState.PostProcessing);

                var prompt = _prompts.GetSelected();
                promptTitle = prompt.Title;
                var rendered = PromptRenderer.Render(prompt.Instructions, transcription);

                finalText = (await _textGeneration
                    .CompleteAsync(rendered, cancellationToken)
                    .ConfigureAwait(false)).Trim();
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                SetState(DictationState.Idle);
                return;
            }

            var durationMs = (long)_timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp()).TotalMilliseconds;

            await _history.AddAsync(
                new HistoryEntry
                {
                    Text = finalText,
                    Transcription = promptTitle is null ? null : transcription,
                    ModelId = _settings.Current.SpeechToText.ModelId,
                    PromptTitle = promptTitle,
                    DurationMs = durationMs,
                },
                cancellationToken).ConfigureAwait(false);

            SetState(DictationState.Injecting);

            await _injector.TypeAsync(finalText, cancellationToken).ConfigureAwait(false);

            SetState(DictationState.Idle);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            SetState(DictationState.Idle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation failed.");

            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        LastError = message;
        SetState(DictationState.Error);
    }

    private void SetState(DictationState state)
    {
        lock (_gate)
        {
            State = state;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
