using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WayType.Services;
using WayType.ViewModels;
using WayType.Views;

namespace WayType;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StaticServiceProvider.Build();

        StaticServiceProvider.Instance.GetRequiredService<IThemeProvider>().Apply();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = StaticServiceProvider.Instance.GetRequiredService<MainWindow>();
            var mainViewModel = StaticServiceProvider.Instance.GetRequiredService<IMainWindowViewModel>();

            mainWindow.DataContext = mainViewModel;
            mainWindow.Opened += async (_, _) => await mainViewModel.InitializeAsync();

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
