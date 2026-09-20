using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Platform;

namespace WayType.Libraries.Portal;

/// <summary>
/// Stores portal grant tokens on disk, owner-only. Tokens are secrets.
/// </summary>
public sealed class FileRestoreTokenStore(IFileStore fileStore, ILogger<FileRestoreTokenStore> logger) : IRestoreTokenStore
{
    private readonly IFileStore _fileStore = fileStore;
    private readonly ILogger<FileRestoreTokenStore> _logger = logger;

    public string? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var text = _fileStore.ReadAllTextOrNull(path);

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public void Save(string path, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileStore.EnsureDirectory(directory);
        }

        // The store contract is synchronous, so detach the write from any caller sync context before blocking.
        Task.Run(() => _fileStore.WriteAllTextAsync(path, token)).GetAwaiter().GetResult();
        _fileStore.RestrictToOwner(path);

        _logger.LogDebug("Saved a portal token to {Path}.", path);
    }

    public void Delete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _fileStore.DeleteFile(path);

        _logger.LogDebug("Deleted the portal token at {Path}.", path);
    }
}
