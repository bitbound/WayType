namespace WayType.Libraries.Core.Platform;

public interface IFileStore
{
    bool FileExists(string path);

    string ReadAllText(string path);

    string? ReadAllTextOrNull(string path);

    byte[] ReadAllBytes(string path);

    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    Task WriteBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default);

    void DeleteFile(string path);

    void EnsureDirectory(string path);

    void RestrictToOwner(string path);

    string[] GetFileNames(string directory, string searchPattern);

    void ReplaceFile(string sourcePath, string destinationPath);
}
