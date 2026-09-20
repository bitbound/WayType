using Avalonia.Controls;

namespace WayType.ViewModels;

public interface IViewModelBase
{
    Type ViewType { get; }

    Task InitializeAsync();
}

public abstract class ViewModelBase<TView> : ObservableObject, IViewModelBase
    where TView : Control
{
    public Type ViewType => typeof(TView);

    public Task InitializeAsync()
    {
        return OnInitializeAsync();
    }

    protected virtual Task OnInitializeAsync()
    {
        return Task.CompletedTask;
    }
}
