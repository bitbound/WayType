using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WayType.Services;

namespace WayType.Views;

/// <summary>
/// Renders snackbar messages as a stack of fading cards. It subscribes on attach, so it only
/// receives messages while it is part of the visual tree.
/// </summary>
public partial class SnackbarHost : UserControl
{
    /// <summary>
    /// Longer stacks than this just become a wall of text, so the oldest is evicted.
    /// </summary>
    private const int MaxVisible = 3;

    private readonly ISnackbarService _snackbars;

    /// <summary>
    /// Used when the host is created from XAML, which cannot supply constructor arguments. The
    /// container is already built by the time a view is loaded.
    /// </summary>
    public SnackbarHost()
        : this(StaticServiceProvider.Instance.GetRequiredService<ISnackbarService>())
    {
    }

    public SnackbarHost(ISnackbarService snackbars)
    {
        _snackbars = snackbars;

        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _snackbars.Shown += OnShown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _snackbars.Shown -= OnShown;
    }

    private void OnShown(object? sender, SnackbarMessage message)
    {
        Dispatcher.UIThread.Post(() => Add(message));
    }

    private void Add(SnackbarMessage message)
    {
        var stack = this.FindControl<StackPanel>("SnackStack");

        if (stack is null)
        {
            return;
        }

        var card = new Border { Classes = { "snack" } };

        switch (message.Kind)
        {
            case SnackbarKind.Success:
                card.Classes.Add("success");
                break;
            case SnackbarKind.Error:
                card.Classes.Add("error");
                break;
        }

        card.Child = new TextBlock
        {
            Text = message.Text,
            Classes = { "snackText" },
        };

        stack.Children.Add(card);

        while (stack.Children.Count > MaxVisible)
        {
            stack.Children.RemoveAt(0);
        }

        // Fade in on the next frame, so the transition has a starting value to animate from.
        Dispatcher.UIThread.Post(() => card.Opacity = 1, DispatcherPriority.Loaded);

        var timer = new DispatcherTimer { Interval = message.Duration };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Remove(stack, card);
        };

        timer.Start();
    }

    private static void Remove(StackPanel stack, Border card)
    {
        card.Opacity = 0;

        // Give the fade time to run before the element leaves the tree.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            stack.Children.Remove(card);
        };

        timer.Start();
    }
}
