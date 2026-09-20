using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Microsoft.Extensions.DependencyInjection;
using WayType.ViewModels;

namespace WayType;

/// <summary>
/// Resolves the view for a view model through the container, then gives it the view model as data context.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is not IViewModelBase viewModel)
        {
            return new TextBlock { Text = "Not a view model: " + data?.GetType().Name };
        }

        var view = ActivatorUtilities.GetServiceOrCreateInstance(StaticServiceProvider.Instance, viewModel.ViewType);

        if (view is not Control control)
        {
            return new TextBlock { Text = "View is not a control: " + viewModel.ViewType.Name };
        }

        control.DataContext = viewModel;

        return control;
    }

    public bool Match(object? data)
    {
        return data is IViewModelBase;
    }
}
