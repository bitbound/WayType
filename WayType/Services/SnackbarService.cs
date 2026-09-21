namespace WayType.Services;

public enum SnackbarKind
{
    Info,
    Success,
    Error,
}

public sealed record SnackbarMessage(string Text, SnackbarKind Kind, TimeSpan Duration);

public interface ISnackbarService
{
    /// <summary>
    /// Queues a short message for the in-app notification area.
    /// </summary>
    void Show(string text, SnackbarKind kind = SnackbarKind.Info);

    event EventHandler<SnackbarMessage>? Shown;
}

/// <summary>
/// Broadcasts snackbar messages to whatever is hosting them. It keeps no queue of its own, so a
/// message raised before the host exists is simply dropped rather than replayed later.
/// </summary>
public sealed class SnackbarService : ISnackbarService
{
    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(3.5);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(6);

    public event EventHandler<SnackbarMessage>? Shown;

    public void Show(string text, SnackbarKind kind = SnackbarKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var duration = kind == SnackbarKind.Error ? ErrorDuration : InfoDuration;

        Shown?.Invoke(this, new SnackbarMessage(text, kind, duration));
    }
}
