using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WayType.ViewModels;

namespace WayType.Views;

public partial class SettingsView : UserControl
{
    private TopLevel? _topLevel;

    public SettingsView()
    {
        InitializeComponent();
    }

    // The window's tunnel phase runs before any focused control sees the key, so a combination can be
    // pressed without focusing anything and the keystrokes never reach the text fields behind it.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(
            InputElement.KeyDownEvent,
            OnCaptureKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnCaptureKeyDown);
            _topLevel = null;
        }
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel { IsCapturingHotkey: true } viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            viewModel.CancelHotkeyCapture();
            e.Handled = true;
            return;
        }

        if (HotkeyKeyFormatter.IsWaitingForKey(e.Key))
        {
            return;
        }

        e.Handled = true;

        if (HotkeyKeyFormatter.TryFormat(e.Key, e.KeyModifiers, out var combo))
        {
            viewModel.CompleteHotkeyCapture(combo);
            return;
        }

        viewModel.FailHotkeyCapture($"{e.Key} can't be the key in a WayType shortcut.");
    }
}
