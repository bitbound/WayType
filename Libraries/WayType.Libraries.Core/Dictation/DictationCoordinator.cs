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
    private const float MinimumActiveSampleRatio = 0.01f;

    private readonly ITextInputInjector _injector;
    private readonly ILogger<DictationCoordinator> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly IAudioRecorder _recorder;
    private readonly IRecordingStore _recordings;
    private readonly ISettingsService _settings;
    private readonly IPromptService _prompts;
    private readonly IHistoryService _history;
    private readonly ISpeechToTextClient _speechToText;
    private readonly ITextGenerationClient _textGeneration;

    private CancellationTokenSource? _endOfRecording;
    private Task? _activeRun;

    public DictationCoordinator(
        IAudioRecorder recorder,
        IRecordingStore recordings,
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
        _recordings = recordings;
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

        if (CanStart(State))
        {
            await StartAsync(cancellationToken);
        }
    }

    // Error has to be restartable or a single failed take silences the hotkey until the app is restarted:
    // nothing else ever moves the state back off Error.
    private static bool CanStart(DictationState state)
    {
        return state is DictationState.Idle or DictationState.Error;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!CanStart(State))
            {
                return Task.CompletedTask;
            }

            if (!_settings.Current.SpeechToText.IsConfigured)
            {
                Fail("Configure the speech-to-text endpoint and model in Settings first.");
                return Task.CompletedTask;
            }

            LastError = null;
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
                    .RecordAsync(_settings.Current.InputDeviceId, endOfRecording.Token, maxDuration, cancellationToken)
                    .ConfigureAwait(false);
            }

            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            var silenceThreshold = Math.Clamp(
                _settings.Current.SilenceRmsThreshold,
                0.0001f,
                0.1f);

            var audioStats = GetAudioStats(pcm, silenceThreshold);
            _logger.LogDebug(
                "Captured {Frames} frames with peak {Peak:0.0000}, RMS {Rms:0.0000}, and active ratio {ActiveRatio:0.0000}.",
                pcm.FrameCount,
                audioStats.Peak,
                audioStats.Rms,
                audioStats.ActiveRatio);

            if (pcm.FrameCount == 0
                || audioStats.Rms <= silenceThreshold
                || audioStats.ActiveRatio < MinimumActiveSampleRatio)
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
            var entryId = Guid.NewGuid();
            var audioFileName = await SaveRecordingAsync(entryId, wav, cancellationToken).ConfigureAwait(false);

            try
            {
                await _history.AddAsync(
                    new HistoryEntry
                    {
                        Id = entryId,
                        Text = finalText,
                        Transcription = promptTitle is null ? null : transcription,
                        ModelId = _settings.Current.SpeechToText.ModelId,
                        PromptTitle = promptTitle,
                        AudioFileName = audioFileName,
                        DurationMs = durationMs,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The audio is only reachable through the row, so it goes back out with the row.
                _recordings.Delete(audioFileName);

                throw;
            }

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

    // The WAV is already rendered for the endpoint, so keeping it costs a copy rather than another
    // capture. A failure to write it is not worth losing a dictation that transcribed fine.
    private async Task<string?> SaveRecordingAsync(Guid entryId, byte[] wav, CancellationToken cancellationToken)
    {
        if (!_settings.Current.KeepRecordings)
        {
            return null;
        }

        try
        {
            return await _recordings.SaveAsync(entryId, wav, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not save the recording for history.");

            return null;
        }
    }

    private void Fail(string message)
    {
        LastError = message;
        SetState(DictationState.Error);
    }

    private static (float Peak, float Rms, float ActiveRatio) GetAudioStats(PcmAudio audio, float silenceThreshold)
    {
        var peak = 0f;
        var sumOfSquares = 0d;
        var finiteSamples = 0;
        var activeSamples = 0;

        foreach (var sample in audio.InterleavedSamples)
        {
            if (!float.IsFinite(sample))
            {
                continue;
            }

            var magnitude = MathF.Abs(sample);
            peak = MathF.Max(peak, magnitude);
            sumOfSquares += sample * sample;
            finiteSamples++;

            if (magnitude > silenceThreshold)
            {
                activeSamples++;
            }
        }

        return finiteSamples == 0
            ? (0, 0, 0)
            : (peak, (float)Math.Sqrt(sumOfSquares / finiteSamples), (float)activeSamples / finiteSamples);
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
