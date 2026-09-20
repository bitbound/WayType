namespace WayType.Libraries.Core.Platform;

/// <summary>
/// Persists portal grant state so the compositor does not re-prompt on every launch.
/// Tokens are secrets and are stored owner-only.
/// </summary>
public interface IRestoreTokenStore
{
    string? Read(string path);

    void Save(string path, string token);

    void Delete(string path);
}
