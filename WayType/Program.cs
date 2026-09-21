using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Updater;

namespace WayType;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (TryRunUpdateHandoff(args))
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Set to "wayland" to try the native Wayland backend. X11, through XWayland on a Wayland
    /// session, is the default because Avalonia's Wayland backend leaves these unimplemented and the
    /// dictation indicator needs all of them: positioning a window, keeping it above others, keeping
    /// it out of the task list, and showing it without activating it.
    /// </summary>
    private const string WindowingOverrideVariable = "WAYTYPE_WINDOWING";

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

        // Opt-in only. UseWaylandWithFallback still falls back to the X11 backend configured above if
        // the compositor cannot be used, so this cannot leave the app with no windowing system.
        if (string.Equals(Environment.GetEnvironmentVariable(WindowingOverrideVariable), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.UseWaylandWithFallback();
        }

        return builder
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
    }

    private static bool TryRunUpdateHandoff(string[] args)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
        services.AddSingleton<UpdateHandoffRunner>();

        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<UpdateHandoffRunner>();

        if (!runner.IsRequested(args))
        {
            return false;
        }

        return runner.RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
    }
}
