namespace WayType.Libraries.Core.Input;

public interface IGlobalHotkeySource : IAsyncDisposable
{
    bool IsSupported { get; }

    event EventHandler? Activated;

    /// <summary>
    /// Binds the shortcut through the portal, returning false when the compositor rejects it.
    /// </summary>
    Task<bool> BindAsync(string shortcut, CancellationToken cancellationToken = default);

    Task UnbindAsync(CancellationToken cancellationToken = default);
}
