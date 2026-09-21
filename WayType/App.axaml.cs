using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Settings;
using WayType.Services;
using WayType.ViewModels;
using WayType.Views;

namespace WayType;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private IMainWindowViewModel? _mainViewModel;
    private bool _isShellInitialized;
    private bool _isExiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StaticServiceProvider.Build();

        StaticServiceProvider.Instance.GetRequiredService<IThemeProvider>().Apply();
        ApplyLogLevel();

        // The floating indicator has no window of its own to open, so it is started here and only
        // creates its window the first time a dictation runs.
        StaticServiceProvider.Instance.GetRequiredService<IStatusOverlayController>().Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Dictation keeps running from the tray while the window is hidden, so closing every
            // window must not end the process on its own.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mainWindow = StaticServiceProvider.Instance.GetRequiredService<MainWindow>();
            var mainViewModel = StaticServiceProvider.Instance.GetRequiredService<IMainWindowViewModel>();

            _mainWindow = mainWindow;
            _mainViewModel = mainViewModel;
            mainWindow.DataContext = mainViewModel;
            mainWindow.Opened += OnMainWindowOpened;
            mainWindow.Closing += OnMainWindowClosing;

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The saved verbosity has to be applied before the shell starts, or the startup sequence is
    // missing from the log exactly when it is most useful.
    private static void ApplyLogLevel()
    {
        var settings = StaticServiceProvider.Instance.GetRequiredService<ISettingsService>().Current;

        StaticServiceProvider.Instance
            .GetRequiredService<LogLevelSwitch>()
            .Set(settings.DebugLogging ? LogLevel.Debug : LogLevel.Information);
    }

    // Opened fires on every show, and hiding to the tray then showing again counts. The shell view
    // model is a singleton, so initializing per show appends a second set of nav items and rebinds
    // the hotkey again.
    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (_isShellInitialized)
        {
            return;
        }

        _isShellInitialized = true;

        _ = _mainViewModel?.InitializeAsync();
    }

    // Closing the window parks it in the tray. The global shortcut keeps working from there.
    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OnTrayOpenClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OnTrayQuitClick(object? sender, EventArgs e)
    {
        _isExiting = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }
}
