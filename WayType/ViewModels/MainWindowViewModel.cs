using System.Collections.ObjectModel;
using Avalonia.Threading;
using WayType.Libraries.Core.Dictation;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Updates;
using WayType.Views;

namespace WayType.ViewModels;

public interface IMainWindowViewModel : IViewModelBase
{
    ObservableCollection<NavItemViewModel> NavigationItems { get; }

    IViewModelBase? CurrentViewModel { get; set; }
}

public partial class MainWindowViewModel : ViewModelBase<MainWindow>, IMainWindowViewModel
{
    private readonly IDictationCoordinator _dictation;
    private readonly IGlobalHotkeySource _hotkeys;
    private readonly INavigationProvider _navigation;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settings;
    private readonly IUpdateService _updates;

    [ObservableProperty]
    private IViewModelBase? _currentViewModel;

    [ObservableProperty]
    private string _dictationStatus = "Idle";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private string? _statusError;

    [ObservableProperty]
    private string? _hotkeyError;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _updateVersion;

    public MainWindowViewModel(
        INavigationProvider navigation,
        IServiceProvider serviceProvider,
        IDictationCoordinator dictation,
        IGlobalHotkeySource hotkeys,
        ISettingsService settings,
        IUpdateService updates)
    {
        _navigation = navigation;
        _serviceProvider = serviceProvider;
        _dictation = dictation;
        _hotkeys = hotkeys;
        _settings = settings;
        _updates = updates;

        _dictation.StateChanged += (_, _) => Dispatcher.UIThread.Post(RefreshDictationStatus);
        _hotkeys.Activated += (_, _) => Dispatcher.UIThread.Post(OnHotkeyActivated);
        _updates.UpdateAvailable += (_, info) => Dispatcher.UIThread.Post(() => ShowUpdate(info));
    }

    public ObservableCollection<NavItemViewModel> NavigationItems { get; } = [];

    protected override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        NavigationItems.Add(new NavItemViewModel("settings_regular", "Settings", _navigation, typeof(SettingsViewModel)));
        NavigationItems.Add(new NavItemViewModel("history_regular", "History", _navigation, typeof(HistoryViewModel)));
        NavigationItems.Add(new NavItemViewModel("info_regular", "About", _navigation, typeof(AboutViewModel)));

        await _navigation.NavigateTo<SettingsViewModel>();

        RefreshDictationStatus();
        _ = StartHotkeyAsync();
        _ = CheckForUpdatesAsync();
    }

    partial void OnIsListeningChanged(bool value)
    {
        if (value)
        {
            DictationStatus = "Listening";
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        await _updates.ApplyAsync();
    }

    private async Task StartHotkeyAsync()
    {
        HotkeyError = _settings.Current.TriggerMode == ShortcutTriggerMode.Press
            ? "Hold-to-talk needs a key release event, which the portal shortcut API does not deliver. Tap mode is used instead."
            : null;

        try
        {
            var bound = await _hotkeys.BindAsync(_settings.Current.Hotkey);

            if (!bound)
            {
                HotkeyError = "The compositor rejected the shortcut. Check the log for the portal response.";
            }
        }
        catch (Exception ex)
        {
            HotkeyError = ex.Message;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.Current.CheckForUpdates)
        {
            return;
        }

        try
        {
            var update = await _updates.CheckAsync();

            if (update is not null)
            {
                ShowUpdate(update);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex}");
        }
    }

    private void ShowUpdate(UpdateInfo info)
    {
        IsUpdateAvailable = true;
        UpdateVersion = info.Version;
    }

    private void OnHotkeyActivated()
    {
        _ = _dictation.ToggleAsync();
    }

    private void RefreshDictationStatus()
    {
        IsWorking = _dictation.State is DictationState.Transcribing or DictationState.PostProcessing or DictationState.Injecting;
        IsListening = _dictation.IsListening;

        StatusError = _dictation.State == DictationState.Error ? _dictation.LastError : null;

        if (_dictation.State == DictationState.Error)
        {
            DictationStatus = "Failed";
            return;
        }

        DictationStatus = _dictation.State switch
        {
            DictationState.Listening => "Listening",
            DictationState.Transcribing => "Transcribing",
            DictationState.PostProcessing => "Post-processing",
            DictationState.Injecting => "Typing",
            _ => "Idle",
        };
    }
}
