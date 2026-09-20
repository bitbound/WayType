namespace WayType.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly INavigationProvider _navigation;
    private readonly Type _destinationType;

    [ObservableProperty]
    private bool _isSelected;

    public NavItemViewModel(string iconKey, string label, INavigationProvider navigation, Type destinationType)
    {
        IconKey = iconKey;
        Label = label;
        _navigation = navigation;
        _destinationType = destinationType;

        navigation.ActiveViewModelTypeChanged += OnActiveViewModelTypeChanged;
    }

    public string IconKey { get; }

    public string Label { get; }

    [RelayCommand]
    private Task Navigate()
    {
        return _navigation.NavigateTo(_destinationType);
    }

    private void OnActiveViewModelTypeChanged(Type? type)
    {
        IsSelected = type == _destinationType;
    }
}
