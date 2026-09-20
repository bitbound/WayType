namespace WayType.Libraries.Core.Input;

public interface ITextInputInjector
{
    /// <summary>
    /// True when a portal restore token exists on disk. It does not prove the grant is still valid.
    /// </summary>
    bool HasSavedGrant { get; }

    /// <summary>
    /// Validates the saved grant without prompting the user, rotating the stored token when it succeeds.
    /// </summary>
    Task<bool> ProbeGrantAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the compositor authorize dialog and stores the returned restore token.
    /// </summary>
    Task<bool> RequestGrantAsync(CancellationToken cancellationToken = default);

    Task RevokeGrantAsync(CancellationToken cancellationToken = default);

    Task TypeAsync(string text, CancellationToken cancellationToken = default);
}
