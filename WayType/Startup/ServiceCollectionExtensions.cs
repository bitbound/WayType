using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Core.Dictation;
using WayType.Libraries.Core.History;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Prompts;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Speech;
using WayType.Libraries.Core.Theming;
using WayType.Libraries.Core.Updates;
using WayType.Libraries.Native.Linux;
using WayType.Libraries.Portal;
using WayType.Libraries.Speech;
using WayType.Libraries.Updater;
using WayType.Services;
using WayType.ViewModels;
using WayType.Views;

namespace WayType.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWayType(this IServiceCollection services)
    {
        // Built up front because logging needs the path before the container exists, and the same
        // instance is registered below so every consumer sees one set of paths.
        var paths = new PlatformPaths();
        var logLevel = new LogLevelSwitch(LogLevel.Information);
        var fileLogger = new FileLoggerProvider(paths.ConfigDirectory, logLevel);

        // The app is a windowed executable with no console attached, so the file sink is the only
        // place diagnostics survive. The console sink is kept for running from a terminal.
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.AddProvider(fileLogger);
            builder.AddFilter((_, level) => logLevel.IsEnabled(level));
        });

        services.AddSingleton(logLevel);
        services.AddSingleton(fileLogger);

        services.AddSingleton<IPlatformPaths>(paths);
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<IRestoreTokenStore, FileRestoreTokenStore>();
        services.AddSingleton<IAppInfo, AssemblyAppInfo>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WayTypeHttp>();
        services.AddSingleton(sp => sp.GetRequiredService<WayTypeHttp>().ForSpeech);
        services.AddSingleton(sp => sp.GetRequiredService<WayTypeHttp>().ForText);

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPromptService, PromptService>();
        services.AddSingleton<IRecordingStore, RecordingStore>();
        services.AddSingleton<IHistoryService, HistoryService>();

        services.AddSingleton<IKeysymResolver, XkbKeysymResolver>();
        services.AddSingleton<IKeycodeResolver, EvdevKeycodeResolver>();
        services.AddSingleton<IAudioRecorder, PulseAudioRecorder>();
        services.AddSingleton<IAudioPlayer, PulseAudioPlayer>();
        services.AddSingleton<IAudioCaptureDeviceEnumerator, PulseAudioDeviceEnumerator>();

        services.AddSingleton<ISpeechToTextClient, OpenAiSpeechToTextClient>();
        services.AddSingleton<ITextGenerationClient, OpenAiTextGenerationClient>();

        services.AddSingleton<ITextInputInjector, RemoteDesktopTextInjector>();
        services.AddSingleton<IGlobalHotkeySource, PortalGlobalHotkeySource>();
        services.AddSingleton<ISystemColorSchemeSource, PortalSystemColorSchemeSource>();

        services.AddSingleton<IUpdateService, GitHubReleaseUpdateService>();
        services.AddSingleton<UpdateHandoffRunner>();

        services.AddSingleton<IDictationCoordinator, DictationCoordinator>();
        services.AddSingleton<IStatusOverlayController, StatusOverlayController>();

        services.AddSingleton<IThemeProvider, ThemeProvider>();
        services.AddSingleton<INavigationProvider, NavigationProvider>();

        services.AddSingleton<IMainWindowViewModel, MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<AboutViewModel>();

        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsView>();
        services.AddTransient<HistoryView>();
        services.AddTransient<AboutView>();

        return services;
    }
}
