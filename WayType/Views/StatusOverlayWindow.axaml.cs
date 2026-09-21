using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace WayType.Views;

/// <summary>
/// A small always-on-top pill that shows what dictation is doing. It never takes focus, so typing
/// into another window is not interrupted while it is visible.
/// </summary>
public partial class StatusOverlayWindow : Window
{
    public StatusOverlayWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Sets the label and its colour. The icon resource is swapped by key rather than by binding so
    /// the window needs no view model.
    /// </summary>
    public void SetStatus(string text, string iconKey, IBrush accent)
    {
        var label = this.FindControl<TextBlock>("StatusText");
        var icon = this.FindControl<PathIcon>("MicIcon");

        if (label is not null)
        {
            label.Text = text;
        }

        if (icon is null)
        {
            return;
        }

        icon.Foreground = accent;

        if (Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is StreamGeometry stream)
        {
            icon.Data = stream;
        }
    }
}
