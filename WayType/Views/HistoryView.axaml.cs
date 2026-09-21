using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using WayType.Services;
using WayType.ViewModels;

namespace WayType.Views;

public partial class HistoryView : UserControl
{
    private readonly ISnackbarService _snackbars;

    /// <summary>
    /// Used when the view is created from XAML, which cannot supply constructor arguments. The
    /// container is already built by the time a view is loaded.
    /// </summary>
    public HistoryView()
        : this(StaticServiceProvider.Instance.GetRequiredService<ISnackbarService>())
    {
    }

    public HistoryView(ISnackbarService snackbars)
    {
        _snackbars = snackbars;

        InitializeComponent();
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistoryItemViewModel item })
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is null)
        {
            _snackbars.Show("Could not reach the clipboard.", SnackbarKind.Error);
            return;
        }

        try
        {
            await clipboard.SetTextAsync(item.Text);
            _snackbars.Show("Copied to clipboard.", SnackbarKind.Success);
        }
        catch (Exception ex)
        {
            // A failure here is usually the compositor refusing the selection owner, which is worth
            // showing rather than silently doing nothing.
            _snackbars.Show($"Copy failed: {ex.Message}", SnackbarKind.Error);
        }
    }
}
