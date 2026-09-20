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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

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
