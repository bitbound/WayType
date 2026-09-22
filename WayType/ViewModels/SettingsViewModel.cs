using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Prompts;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;
using WayType.Libraries.Portal;
using WayType.Views;

namespace WayType.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase<SettingsView>
{
    private readonly ITextInputInjector _injector;
    private readonly IAudioCaptureDeviceEnumerator _devices;
    private readonly IPromptService _prompts;
    private readonly ISettingsService _settings;
    private readonly ISpeechToTextClient _speechToText;
    private readonly ITextGenerationClient _textGeneration;
    private readonly DesktopEntryInstaller _desktopEntry;
    private readonly FileLoggerProvider _fileLogger;
    private readonly LogLevelSwitch _logLevel;

    [ObservableProperty]
    private bool _isPermissionGranted;

    [ObservableProperty]
    private bool _isProbingPermission;

    [ObservableProperty]
    private bool _isRequestingPermission;

    [ObservableProperty]
    private string? _permissionMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private string _hotkey = AppSettings.DefaultHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    private string? _hotkeyCaptureMessage;

    [ObservableProperty]
    private string _triggerModeName = "Tap";

    [ObservableProperty]
    private string _themeModeName = "System";

    [ObservableProperty]
    private int _historyItemsToKeep = 50;

    [ObservableProperty]
    private bool _keepRecordings = true;

    [ObservableProperty]
    private int _maximumRecordingSeconds = 120;

    [ObservableProperty]
    private float _silenceRmsThreshold = AppSettings.DefaultSilenceRmsThreshold;

    [ObservableProperty]
    private int _typingDelayMs = AppSettings.DefaultTypingDelayMs;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private bool _debugLogging;

    [ObservableProperty]
    private ObservableCollection<DeviceOption> _deviceOptions = [];

    [ObservableProperty]
    private DeviceOption? _selectedDevice;

    [ObservableProperty]
    private string? _speechEndpoint;

    [ObservableProperty]
    private string? _speechApiKey;

    [ObservableProperty]
    private string? _speechModelId;

    [ObservableProperty]
    private ObservableCollection<string> _speechModels = [];

    [ObservableProperty]
    private bool _isLoadingSpeechModels;

    [ObservableProperty]
    private string? _speechModelsMessage;

    [ObservableProperty]
    private bool _postProcessingEnabled;

    [ObservableProperty]
    private int _postTimeoutSeconds = PostProcessingSettings.DefaultTimeoutSeconds;

    [ObservableProperty]
    private string? _postEndpoint;

    [ObservableProperty]
    private string? _postApiKey;

    [ObservableProperty]
    private string? _postModelId;

    [ObservableProperty]
    private ObservableCollection<string> _postModels = [];

    [ObservableProperty]
    private bool _isLoadingPostModels;

    [ObservableProperty]
    private string? _postModelsMessage;

    [ObservableProperty]
    private string _thinkingMode = "Not set";

    [ObservableProperty]
    private string _reasoningEffort = "Not set";

    [ObservableProperty]
    private string _temperature = string.Empty;

    [ObservableProperty]
    private string _topP = string.Empty;

    [ObservableProperty]
    private string _maxCompletionTokens = string.Empty;

    [ObservableProperty]
    private string _maxTokens = string.Empty;

    [ObservableProperty]
    private string _frequencyPenalty = string.Empty;

    [ObservableProperty]
    private string _presencePenalty = string.Empty;

    [ObservableProperty]
    private string _repetitionPenalty = string.Empty;

    [ObservableProperty]
    private string _seed = string.Empty;

    [ObservableProperty]
    private string _nChoices = string.Empty;

    [ObservableProperty]
    private string _stopSequence = string.Empty;

    [ObservableProperty]
    private string _responseFormat = string.Empty;

    [ObservableProperty]
    private string _advancedOverridesJson = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PromptOption> _promptOptions = [];

    [ObservableProperty]
    private PromptOption? _selectedPrompt;

    [ObservableProperty]
    private string _promptTitle = string.Empty;

    [ObservableProperty]
    private string _promptInstructions = string.Empty;

    [ObservableProperty]
    private bool _isPromptReadOnly;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _saveMessage;

    [ObservableProperty]
    private string? _saveError;

    [ObservableProperty]
    private string? _shortcutMessage;

    private DispatcherTimer? _saveMessageTimer;

    public SettingsViewModel(
        ISettingsService settings,
        ITextInputInjector injector,
        IAudioCaptureDeviceEnumerator devices,
        IPromptService prompts,
        ISpeechToTextClient speechToText,
        ITextGenerationClient textGeneration,
        DesktopEntryInstaller desktopEntry,
        FileLoggerProvider fileLogger,
        LogLevelSwitch logLevel)
    {
        _settings = settings;
        _injector = injector;
        _devices = devices;
        _prompts = prompts;
        _speechToText = speechToText;
        _textGeneration = textGeneration;
        _desktopEntry = desktopEntry;
        _fileLogger = fileLogger;
        _logLevel = logLevel;
    }

    public IReadOnlyList<string> ThinkingModes { get; } = ["Not set", "Enabled", "Disabled"];

    public IReadOnlyList<string> ReasoningEfforts { get; } =
        ["Not set", "none", "minimal", "low", "medium", "high", "xhigh", "max"];

    public IReadOnlyList<string> ThemeModes { get; } = ["System", "Light", "Dark"];

    public IReadOnlyList<string> TriggerModes { get; } = ["Tap", "Press"];

    public IReadOnlyList<string> ResponseFormats { get; } = [string.Empty, "text", "json_object"];

    public string HotkeyDisplay => IsCapturingHotkey
        ? "Press the keys you want to use…"
        : Hotkey;

    public string LogFilePath => _fileLogger.LogFilePath;

    protected override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        LoadFromSettings();
        RefreshPrompts();

        _ = ProbePermissionAsync();
        _ = LoadDevicesAsync();
    }

    partial void OnSelectedPromptChanged(PromptOption? value)
    {
        if (value is null)
        {
            return;
        }

        PromptTitle = value.Prompt.Title;
        PromptInstructions = value.Prompt.Instructions;
        IsPromptReadOnly = value.Prompt.IsBuiltIn;
    }

    [RelayCommand]
    private async Task ProbePermissionAsync()
    {
        IsProbingPermission = true;

        try
        {
            IsPermissionGranted = await _injector.ProbeGrantAsync();
            PermissionMessage = IsPermissionGranted
                ? null
                : "Text input permission has not been granted yet. WayType cannot type into other apps until it is.";
        }
        catch (Exception ex)
        {
            IsPermissionGranted = false;
            PermissionMessage = ex.Message;
        }
        finally
        {
            IsProbingPermission = false;
        }
    }

    [RelayCommand]
    private async Task RequestPermissionAsync()
    {
        IsRequestingPermission = true;

        try
        {
            IsPermissionGranted = await _injector.RequestGrantAsync();
            PermissionMessage = IsPermissionGranted ? "Permission granted." : "The permission request was declined.";
        }
        catch (Exception ex)
        {
            IsPermissionGranted = false;
            PermissionMessage = ex.Message;
        }
        finally
        {
            IsRequestingPermission = false;
        }
    }

    [RelayCommand]
    private async Task RevokePermissionAsync()
    {
        await _injector.RevokeGrantAsync();
        IsPermissionGranted = false;
        PermissionMessage = "Stored permission cleared.";
    }

    [RelayCommand]
    private void CreateShortcut()
    {
        ShortcutMessage = _desktopEntry.TryInstall(showInApplicationMenu: true)
            ? "WayType was added to the application menu."
            : "WayType could not create the application shortcut.";
    }

    [RelayCommand]
    private void BeginHotkeyCapture()
    {
        IsCapturingHotkey = true;
        HotkeyCaptureMessage = null;
    }

    [RelayCommand]
    public void CancelHotkeyCapture()
    {
        IsCapturingHotkey = false;
        HotkeyCaptureMessage = null;
    }

    // Called by the view once a key press has been turned into a shortcut string.
    public void CompleteHotkeyCapture(string combo)
    {
        Hotkey = combo;
        IsCapturingHotkey = false;
        HotkeyCaptureMessage = null;
    }

    public void FailHotkeyCapture(string message)
    {
        HotkeyCaptureMessage = message;
    }

    [RelayCommand]
    private async Task LoadDevicesAsync()
    {
        try
        {
            var found = await _devices.GetDevicesAsync();
            var options = found.Select(device => new DeviceOption(device)).ToList();
            var selectedId = _settings.Current.InputDeviceId;

            DeviceOptions = new ObservableCollection<DeviceOption>(options);
            SelectedDevice = options.FirstOrDefault(option => option.Device.Id == selectedId)
                ?? options.FirstOrDefault(option => option.Device.IsDefault)
                ?? options.FirstOrDefault();
        }
        catch (Exception ex)
        {
            PermissionMessage ??= ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshSpeechModelsAsync()
    {
        IsLoadingSpeechModels = true;
        SpeechModelsMessage = null;

        try
        {
            var models = await _speechToText.ListModelsAsync(SpeechEndpoint, SpeechApiKey);
            SpeechModels = new ObservableCollection<string>(models.Select(model => model.Id).Order(StringComparer.Ordinal));
            SpeechModelsMessage = $"{SpeechModels.Count} models";
        }
        catch (Exception ex)
        {
            SpeechModelsMessage = ex.Message;
        }
        finally
        {
            IsLoadingSpeechModels = false;
        }
    }

    [RelayCommand]
    private async Task RefreshPostModelsAsync()
    {
        IsLoadingPostModels = true;
        PostModelsMessage = null;

        try
        {
            var models = await _textGeneration.ListModelsAsync(PostEndpoint, PostApiKey);
            PostModels = new ObservableCollection<string>(models.Select(model => model.Id).Order(StringComparer.Ordinal));
            PostModelsMessage = $"{PostModels.Count} models";
        }
        catch (Exception ex)
        {
            PostModelsMessage = ex.Message;
        }
        finally
        {
            IsLoadingPostModels = false;
        }
    }

    [RelayCommand]
    private async Task CreatePromptAsync()
    {
        var created = await _prompts.CreateAsync("New prompt");

        RefreshPrompts();
        SelectedPrompt = PromptOptions.FirstOrDefault(option => option.Prompt.Id == created.Id);
    }

    [RelayCommand]
    private async Task DeletePromptAsync()
    {
        if (SelectedPrompt is null || SelectedPrompt.Prompt.IsBuiltIn)
        {
            return;
        }

        await _prompts.DeleteAsync(SelectedPrompt.Prompt.Id);

        RefreshPrompts();
        SelectedPrompt = PromptOptions.FirstOrDefault();
    }

    [RelayCommand]
    private async Task DuplicatePromptAsync()
    {
        if (SelectedPrompt is null)
        {
            return;
        }

        var copy = await _prompts.DuplicateAsync(SelectedPrompt.Prompt.Id);

        if (copy is null)
        {
            return;
        }

        RefreshPrompts();
        SelectedPrompt = PromptOptions.FirstOrDefault(option => option.Prompt.Id == copy.Id);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveError = null;
        SaveMessage = null;

        var parsed = TryReadGenerationOptions(out var options, out var error);

        if (!parsed)
        {
            SaveError = error;
            return;
        }

        var settings = _settings.Current;

        settings.Theme = Enum.TryParse<ThemeMode>(ThemeModeName, out var theme) ? theme : ThemeMode.System;
        settings.TriggerMode = Enum.TryParse<ShortcutTriggerMode>(TriggerModeName, out var trigger)
            ? trigger
            : ShortcutTriggerMode.Tap;
        settings.Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? AppSettings.DefaultHotkey : Hotkey.Trim();
        settings.HistoryItemsToKeep = Math.Clamp(HistoryItemsToKeep, 0, 5000);
        settings.KeepRecordings = KeepRecordings;
        settings.MaximumRecordingSeconds = Math.Clamp(MaximumRecordingSeconds, 1, 3600);
        settings.SilenceRmsThreshold = Math.Clamp(SilenceRmsThreshold, 0.0001f, 0.1f);
        settings.TypingDelayMs = Math.Clamp(TypingDelayMs, 1, 100);
        settings.CheckForUpdates = CheckForUpdates;
        settings.DebugLogging = DebugLogging;
        settings.InputDeviceId = SelectedDevice?.Device.Id;
        settings.InputDeviceName = SelectedDevice?.Device.Name;

        settings.SpeechToText.Endpoint = SpeechEndpoint?.Trim();
        settings.SpeechToText.ApiKey = SpeechApiKey?.Trim();
        settings.SpeechToText.ModelId = SpeechModelId?.Trim();

        settings.PostProcessing.Enabled = PostProcessingEnabled;
        settings.PostProcessing.Endpoint = PostEndpoint?.Trim();
        settings.PostProcessing.ApiKey = PostApiKey?.Trim();
        settings.PostProcessing.ModelId = PostModelId?.Trim();
        settings.PostProcessing.TimeoutSeconds = Math.Clamp(PostTimeoutSeconds, 1, 3600);
        settings.PostProcessing.Options = options;
        settings.PostProcessing.SelectedPromptId = SelectedPrompt?.Prompt.Id == TranscriptionPrompt.BuiltInId
            ? null
            : SelectedPrompt?.Prompt.Id;

        if (SelectedPrompt is { Prompt.IsBuiltIn: false } prompt)
        {
            prompt.Prompt.Title = PromptTitle;
            prompt.Prompt.Instructions = PromptInstructions;

            await _prompts.UpdateAsync(prompt.Prompt);

            // The dropdown shows the cached title, so it needs rebuilding after a rename.
            RefreshPrompts();
        }

        await _settings.SaveAsync();

        ApplyLogLevel();

        SaveMessage = "Saved.";
    }

    private void ApplyLogLevel()
    {
        _logLevel.Set(DebugLogging ? LogLevel.Debug : LogLevel.Information);
    }

    /// <summary>
    /// Opens the log file so a report of "it just stopped working" can be checked against what the
    /// app actually did.
    /// </summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_fileLogger.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SaveError = $"Could not open the log file: {ex.Message}";
        }
    }

    // A note that sits there forever cannot show that a later click did anything, so it clears itself.
    partial void OnSaveMessageChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        _saveMessageTimer?.Stop();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveMessage = null;
        };

        _saveMessageTimer = timer;
        timer.Start();
    }

    private void LoadFromSettings()
    {
        var settings = _settings.Current;

        ThemeModeName = settings.Theme.ToString();
        TriggerModeName = settings.TriggerMode.ToString();
        Hotkey = settings.Hotkey;
        HistoryItemsToKeep = settings.HistoryItemsToKeep;
        KeepRecordings = settings.KeepRecordings;
        MaximumRecordingSeconds = settings.MaximumRecordingSeconds;
        SilenceRmsThreshold = settings.SilenceRmsThreshold;
        TypingDelayMs = settings.TypingDelayMs;
        CheckForUpdates = settings.CheckForUpdates;
        DebugLogging = settings.DebugLogging;

        SpeechEndpoint = settings.SpeechToText.Endpoint;
        SpeechApiKey = settings.SpeechToText.ApiKey;
        SpeechModelId = settings.SpeechToText.ModelId;

        PostProcessingEnabled = settings.PostProcessing.Enabled;
        PostEndpoint = settings.PostProcessing.Endpoint;
        PostApiKey = settings.PostProcessing.ApiKey;
        PostModelId = settings.PostProcessing.ModelId;
        PostTimeoutSeconds = settings.PostProcessing.TimeoutSeconds;

        var options = settings.PostProcessing.Options;

        ThinkingMode = options.ThinkingEnabled switch
        {
            null => "Not set",
            true => "Enabled",
            false => "Disabled",
        };
        ReasoningEffort = string.IsNullOrWhiteSpace(options.ReasoningEffort) ? "Not set" : options.ReasoningEffort;
        Temperature = options.Temperature?.ToString() ?? string.Empty;
        TopP = options.TopP?.ToString() ?? string.Empty;
        MaxCompletionTokens = options.MaxCompletionTokens?.ToString() ?? string.Empty;
        MaxTokens = options.MaxTokens?.ToString() ?? string.Empty;
        FrequencyPenalty = options.FrequencyPenalty?.ToString() ?? string.Empty;
        PresencePenalty = options.PresencePenalty?.ToString() ?? string.Empty;
        RepetitionPenalty = options.RepetitionPenalty?.ToString() ?? string.Empty;
        Seed = options.Seed?.ToString() ?? string.Empty;
        NChoices = options.N?.ToString() ?? string.Empty;
        StopSequence = options.Stop ?? string.Empty;
        ResponseFormat = options.ResponseFormat ?? string.Empty;
        AdvancedOverridesJson = options.AdvancedOverridesJson ?? string.Empty;
    }

    private void RefreshPrompts()
    {
        var selectedId = SelectedPrompt?.Prompt.Id ?? _settings.Current.PostProcessing.SelectedPromptId;

        PromptOptions = new ObservableCollection<PromptOption>(
            _prompts.GetAll().Select(prompt => new PromptOption(prompt)));

        SelectedPrompt = PromptOptions.FirstOrDefault(option => option.Prompt.Id == selectedId)
            ?? PromptOptions.FirstOrDefault();
    }

    private bool TryReadGenerationOptions(out TextGenerationOptions options, out string? error)
    {
        options = new TextGenerationOptions
        {
            ThinkingEnabled = ThinkingMode switch
            {
                "Enabled" => true,
                "Disabled" => false,
                _ => null,
            },
            ReasoningEffort = ReasoningEffort == "Not set" ? null : ReasoningEffort,
            Stop = string.IsNullOrWhiteSpace(StopSequence) ? null : StopSequence.Trim(),
            ResponseFormat = string.IsNullOrWhiteSpace(ResponseFormat) ? null : ResponseFormat.Trim(),
            AdvancedOverridesJson = string.IsNullOrWhiteSpace(AdvancedOverridesJson) ? null : AdvancedOverridesJson.Trim(),
        };

        error = null;

        if (!TryReadDecimal(Temperature, nameof(Temperature), out var temperature)
            || !TryReadDecimal(TopP, nameof(TopP), out var topP)
            || !TryReadDecimal(FrequencyPenalty, nameof(FrequencyPenalty), out var frequencyPenalty)
            || !TryReadDecimal(PresencePenalty, nameof(PresencePenalty), out var presencePenalty)
            || !TryReadDecimal(RepetitionPenalty, nameof(RepetitionPenalty), out var repetitionPenalty))
        {
            error = "Numeric overrides must be numbers or blank.";
            return false;
        }

        if (!TryReadInt32(MaxCompletionTokens, nameof(MaxCompletionTokens), out var maxCompletionTokens)
            || !TryReadInt32(MaxTokens, nameof(MaxTokens), out var maxTokens)
            || !TryReadInt32(Seed, nameof(Seed), out var seed)
            || !TryReadInt32(NChoices, nameof(NChoices), out var nChoices))
        {
            error = "Whole-number overrides must be integers or blank.";
            return false;
        }

        options.Temperature = temperature;
        options.TopP = topP;
        options.FrequencyPenalty = frequencyPenalty;
        options.PresencePenalty = presencePenalty;
        options.RepetitionPenalty = repetitionPenalty;
        options.MaxCompletionTokens = maxCompletionTokens;
        options.MaxTokens = maxTokens;
        options.Seed = seed;
        options.N = nChoices;

        return true;
    }

    private static bool TryReadDecimal(string value, string name, out double? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Set(parsed, out result)
            : false;
    }

    private static bool TryReadInt32(string value, string name, out int? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Set(parsed, out result)
            : false;
    }

    private static bool Set<T>(T value, out T? result)
    {
        result = value;
        return true;
    }

    public sealed record DeviceOption(AudioDevice Device)
    {
        public override string ToString()
        {
            return Device.Name;
        }
    }

    public sealed record PromptOption(TranscriptionPrompt Prompt)
    {
        public override string ToString()
        {
            return Prompt.IsBuiltIn ? $"{Prompt.Title} (built-in)" : Prompt.Title;
        }
    }
}
