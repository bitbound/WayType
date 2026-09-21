using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Dictation;
using WayType.Views;

namespace WayType.Services;

public interface IStatusOverlayController
{
    /// <summary>
    /// Starts mirroring the dictation state into the floating indicator.
    /// </summary>
    void Start();
}

/// <summary>
/// Shows a small floating pill at the bottom centre of the screen while a dictation is running, so
/// the state is visible without switching back to the WayType window.
/// </summary>
public sealed class StatusOverlayController(IDictationCoordinator dictation, ILogger<StatusOverlayController> logger) : IStatusOverlayController
{
    /// <summary>
    /// Gap between the pill and the bottom of the work area. The work area already excludes docks
    /// and panels, so this is only breathing room.
    /// </summary>
    private const double BottomMargin = 28;

    private StatusOverlayWindow? _window;
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        dictation.StateChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        // Only the states where something is actively happening are worth a floating indicator.
        // Typing is included so the pill does not flicker between post-processing and the result.
        var (text, iconKey, accentKey) = dictation.State switch
        {
            DictationState.Listening => ("Listening", "mic_on_regular", "SuccessColor"),
            DictationState.Transcribing => ("Transcribing", "arrow_sync_regular", "PrimaryColor"),
            DictationState.PostProcessing => ("Post-processing", "arrow_sync_regular", "PrimaryColor"),
            DictationState.Injecting => ("Typing", "text_regular", "PrimaryColor"),
            _ => (null, string.Empty, string.Empty),
        };

        if (text is null)
        {
            Hide();
            return;
        }

        Show(text, iconKey, accentKey);
    }

    private void Show(string text, string iconKey, string accentKey)
    {
        try
        {
            if (Application.Current?.TryFindResource(accentKey, out var accent) != true || accent is not IBrush brush)
            {
                brush = Brushes.White;
            }

            _window ??= CreateWindow();

            _window.SetStatus(text, iconKey, brush);

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            Position();
        }
        catch (Exception ex)
        {
            // The indicator is a convenience. It must never break dictation.
            logger.LogWarning(ex, "Could not show the dictation status indicator.");
        }
    }

    private StatusOverlayWindow CreateWindow()
    {
        var window = new StatusOverlayWindow();

        // The size is only known once the content has been measured, so re-centre whenever it changes.
        window.SizeChanged += (_, _) => Position();

        return window;
    }

    private void Hide()
    {
        if (_window is null || !_window.IsVisible)
        {
            return;
        }

        _window.Hide();
    }

    private void Position()
    {
        if (_window is null || !_window.IsVisible)
        {
            return;
        }

        var screen = _window.Screens.Primary ?? _window.Screens.All.FirstOrDefault();

        if (screen is null)
        {
            return;
        }

        var size = _window.Bounds.Size;

        if (size.Width <= 0 || size.Height <= 0)
        {
            size = _window.DesiredSize;
        }

        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // WorkingArea and Position are in physical pixels, while the window's size is in logical
        // units, so the size has to be scaled before it can be centred.
        var scaling = screen.Scaling;
        var width = (int)Math.Round(size.Width * scaling);
        var height = (int)Math.Round(size.Height * scaling);
        var area = screen.WorkingArea;

        var x = area.X + ((area.Width - width) / 2);
        var y = area.Y + area.Height - height - (int)Math.Round(BottomMargin * scaling);

        _window.Position = new PixelPoint(x, y);
    }
}
