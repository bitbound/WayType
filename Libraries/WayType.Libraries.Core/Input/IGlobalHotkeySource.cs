namespace WayType.Libraries.Core.Input;

public interface IGlobalHotkeySource : IAsyncDisposable
{
    bool IsSupported { get; }

    event EventHandler? Activated;

    /// <summary>
    /// Raised when the shortcut stops being held. A backend can also send it when a registration goes away,
    /// so consumers must ignore it unless they are mid-press.
    /// </summary>
    event EventHandler? Deactivated;

    /// <summary>
    /// Raised when the binding is lost because the backend connection dropped. The shortcut stays
    /// registered in settings, so the consumer is expected to bind it again.
    /// </summary>
    event EventHandler? BindingLost;

    /// <summary>
    /// Binds the shortcut through the portal, returning false when the compositor rejects it.
    /// </summary>
    Task<bool> BindAsync(string shortcut, CancellationToken cancellationToken = default);

    Task UnbindAsync(CancellationToken cancellationToken = default);
}
