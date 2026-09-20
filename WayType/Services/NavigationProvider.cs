using Microsoft.Extensions.DependencyInjection;

namespace WayType.Services;

public interface INavigationProvider
{
    event Action<Type?>? ActiveViewModelTypeChanged;

    Task NavigateTo<TViewModel>()
        where TViewModel : IViewModelBase;

    Task NavigateTo(Type viewModelType);

    void SetActiveViewModelType(Type type);
}

public sealed class NavigationProvider(
    IServiceProvider serviceProvider,
    IMainWindowViewModel mainWindow) : INavigationProvider
{
    public event Action<Type?>? ActiveViewModelTypeChanged;

    public Task NavigateTo<TViewModel>()
        where TViewModel : IViewModelBase
    {
        return NavigateTo(typeof(TViewModel));
    }

    public async Task NavigateTo(Type viewModelType)
    {
        var viewModel = (IViewModelBase)serviceProvider.GetRequiredService(viewModelType);

        mainWindow.CurrentViewModel = viewModel;
        SetActiveViewModelType(viewModelType);

        await viewModel.InitializeAsync();
    }

    public void SetActiveViewModelType(Type type)
    {
        ActiveViewModelTypeChanged?.Invoke(type);
    }
}
