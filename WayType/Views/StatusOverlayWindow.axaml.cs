using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace WayType.Views;

/// <summary>
/// A small always-on-top pill that shows what dictation is doing. It never takes focus, so typing
/// into another window is not interrupted while it is visible.
/// </summary>
public partial class StatusOverlayWindow : Window
{
    private const double BarMinimumHeight = 3;
    private const double BarMaximumHeight = 18;

    private readonly List<Rectangle> _bars = [];

    public StatusOverlayWindow()
    {
        InitializeComponent();

        var panel = this.FindControl<StackPanel>("WavePanel");

        if (panel is not null)
        {
            _bars.AddRange(panel.Children.OfType<Rectangle>());
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Sets the label and icon. The colour follows from the listening class, which the styles map to
    /// a theme brush.
    /// </summary>
    public void SetStatus(string text, string iconKey, bool isListening)
    {
        var label = this.FindControl<TextBlock>("StatusText");
        var icon = this.FindControl<PathIcon>("MicIcon");
        var panel = this.FindControl<StackPanel>("WavePanel");

        if (label is not null)
        {
            label.Text = text;
        }

        if (icon is not null)
        {
            icon.Classes.Set("listening", isListening);

            if (Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is StreamGeometry stream)
            {
                icon.Data = stream;
            }
        }

        if (panel is not null)
        {
            panel.IsVisible = isListening;
            panel.Classes.Set("listening", isListening);
        }

        foreach (var bar in _bars)
        {
            bar.Classes.Set("listening", isListening);
        }

        if (!isListening)
        {
            ResetBars();
        }
    }

    /// <summary>
    /// Drives the waveform. <paramref name="level"/> is a smoothed 0..1 amplitude and
    /// <paramref name="frame"/> advances the per-bar jitter so the bars move independently.
    /// </summary>
    public void SetLevel(double level, int frame)
    {
        if (_bars.Count == 0)
        {
            return;
        }

        for (var index = 0; index < _bars.Count; index++)
        {
            // Each bar sits at a different point in the wave, so a steady tone still ripples rather
            // than freezing into a flat block.
            var phase = frame * 0.35 + (index * 1.1);
            var wobble = 0.65 + (0.35 * Math.Sin(phase));
            var shaped = Math.Sqrt(Math.Clamp(level, 0, 1)) * wobble;
            var height = BarMinimumHeight + (shaped * (BarMaximumHeight - BarMinimumHeight));

            _bars[index].Height = Math.Clamp(height, BarMinimumHeight, BarMaximumHeight);
        }
    }

    private void ResetBars()
    {
        foreach (var bar in _bars)
        {
            bar.Height = BarMinimumHeight;
        }
    }
}
