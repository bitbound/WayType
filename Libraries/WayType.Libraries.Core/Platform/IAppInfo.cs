namespace WayType.Libraries.Core.Platform;

public interface IAppInfo
{
    string ProductName { get; }

    // Reverse-DNS identity the desktop portals scope permissions and global shortcuts to.
    string AppId { get; }

    Version Version { get; }

    string RepositoryUrl { get; }

    string RepositorySlug { get; }
}
