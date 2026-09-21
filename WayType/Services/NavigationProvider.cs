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

// The shell view model is resolved per navigation instead of injected. Its constructor needs this
// service, so taking it here would leave both singletons unresolvable.
public sealed class NavigationProvider(IServiceProvider serviceProvider) : INavigationProvider
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

        serviceProvider.GetRequiredService<IMainWindowViewModel>().CurrentViewModel = viewModel;
        SetActiveViewModelType(viewModelType);

        await viewModel.InitializeAsync();
    }

    public void SetActiveViewModelType(Type type)
    {
        ActiveViewModelTypeChanged?.Invoke(type);
    }
}
