namespace WayType.Libraries.Core.Platform;

public sealed class LocalFileStore : IFileStore
{
    private static readonly UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string? ReadAllTextOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        var stagedPath = Stage(path);

        await using (var stream = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(contents.AsMemory(), cancellationToken);
        }

        Commit(stagedPath, path);
    }

    public async Task WriteBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        var stagedPath = Stage(path);

        await using (var stream = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(contents.AsMemory(), cancellationToken);
        }

        Commit(stagedPath, path);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    public void RestrictToOwner(string path)
    {
        if (File.Exists(path))
        {
            File.SetUnixFileMode(path, OwnerOnly);
        }
    }

    public string[] GetFileNames(string directory, string searchPattern)
    {
        return Directory.Exists(directory) ? Directory.GetFiles(directory, searchPattern) : [];
    }

    public void ReplaceFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath, true);
    }

    private static string Stage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        return path + ".tmp";
    }

    private static void Commit(string stagedPath, string path)
    {
        File.SetUnixFileMode(stagedPath, OwnerOnly);
        File.Move(stagedPath, path, true);
    }
}
