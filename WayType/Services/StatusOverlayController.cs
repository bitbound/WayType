using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
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
public sealed class StatusOverlayController(
    IDictationCoordinator dictation,
    IAudioLevelMeter levelMeter,
    ILogger<StatusOverlayController> logger) : IStatusOverlayController
{
    /// <summary>
    /// Gap between the pill and the bottom of the work area. The work area already excludes docks
    /// and panels, so this is only breathing room.
    /// </summary>
    private const double BottomMargin = 28;

    private const int FramesPerSecond = 30;

    /// <summary>
    /// How fast the bars fall back to rest. Higher settles quicker, which reads as less bouncy.
    /// </summary>
    private const double DecayPerFrame = 0.72;

    private StatusOverlayWindow? _window;
    private DispatcherTimer? _animation;
    private bool _started;

    /// <summary>
    /// Written by the capture thread and read by the UI thread, so it is stored as bits to keep the
    /// read and write indivisible.
    /// </summary>
    private int _levelBits;
    private double _smoothedLevel;
    private int _frame;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        dictation.StateChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        levelMeter.LevelChanged += (_, level) => Interlocked.Exchange(ref _levelBits, BitConverter.SingleToInt32Bits(level));
    }

    private void Refresh()
    {
        // Only the states where something is actively happening are worth a floating indicator.
        // Typing is included so the pill does not flicker between post-processing and the result.
        var (text, iconKey, isListening) = dictation.State switch
        {
            DictationState.Listening => ("Listening", "mic_on_regular", true),
            DictationState.Transcribing => ("Transcribing", "arrow_sync_regular", false),
            DictationState.PostProcessing => ("Post-processing", "arrow_sync_regular", false),
            DictationState.Injecting => ("Typing", "text_regular", false),
            _ => (null, string.Empty, false),
        };

        if (text is null)
        {
            Hide();
            return;
        }

        Show(text, iconKey, isListening);
    }

    private void Show(string text, string iconKey, bool isListening)
    {
        try
        {
            _window ??= CreateWindow();

            _window.SetStatus(text, iconKey, isListening);

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            SetAnimating(isListening);
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

    private void SetAnimating(bool enabled)
    {
        if (enabled)
        {
            if (_animation is not null)
            {
                return;
            }

            _animation = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / FramesPerSecond) };
            _animation.Tick += (_, _) => AdvanceWaveform();
            _animation.Start();

            return;
        }

        if (_animation is null)
        {
            return;
        }

        _animation.Stop();
        _animation = null;
        _smoothedLevel = 0;
        _frame = 0;
    }

    private void AdvanceWaveform()
    {
        if (_window is null)
        {
            return;
        }

        _frame++;

        var level = BitConverter.Int32BitsToSingle(Volatile.Read(ref _levelBits));

        // Rising edges follow the microphone immediately so speech registers at once; falling edges
        // ease down so the bars bounce instead of snapping to nothing between syllables.
        _smoothedLevel = level > _smoothedLevel
            ? level
            : Math.Max(level, _smoothedLevel * DecayPerFrame);

        _window.SetLevel(_smoothedLevel, _frame);
    }

    private void Hide()
    {
        SetAnimating(false);

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
