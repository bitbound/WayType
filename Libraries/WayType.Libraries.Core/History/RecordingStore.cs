using WayType.Libraries.Core.Platform;

namespace WayType.Libraries.Core.History;

public interface IRecordingStore
{
    /// <summary>
    /// Writes the WAV for a history entry and returns the file name to store on that entry.
    /// </summary>
    Task<string> SaveAsync(Guid entryId, byte[] wavBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the WAV for an entry, or null when the name is unusable or the file is gone.
    /// </summary>
    byte[]? Read(string? fileName);

    bool Exists(string? fileName);

    void Delete(string? fileName);
}

/// <summary>
/// Keeps the WAV behind each history entry in the audio directory, alongside history.json.
/// </summary>
public sealed class RecordingStore(IPlatformPaths paths, IFileStore fileStore) : IRecordingStore
{
    private const string Extension = ".wav";

    public async Task<string> SaveAsync(Guid entryId, byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wavBytes);

        paths.EnsureDirectories();

        var fileName = entryId.ToString("N") + Extension;
        var path = Path.Combine(paths.AudioDirectory, fileName);

        await fileStore.WriteBytesAsync(path, wavBytes, cancellationToken);
        fileStore.RestrictToOwner(path);

        return fileName;
    }

    public byte[]? Read(string? fileName)
    {
        var path = Resolve(fileName);

        return path is null || !fileStore.FileExists(path) ? null : fileStore.ReadAllBytes(path);
    }

    public bool Exists(string? fileName)
    {
        var path = Resolve(fileName);

        return path is not null && fileStore.FileExists(path);
    }

    public void Delete(string? fileName)
    {
        var path = Resolve(fileName);

        if (path is not null)
        {
            fileStore.DeleteFile(path);
        }
    }

    // The name comes out of history.json, which anyone with write access to the data directory can
    // edit, so it is only allowed to be a plain file name inside the audio directory.
    private string? Resolve(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        return Path.Combine(paths.AudioDirectory, fileName);
    }
}
