using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using WayType.ViewModels;

namespace WayType.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistoryItemViewModel item })
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(item.Text);
        }
    }
}
