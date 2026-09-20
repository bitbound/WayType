namespace WayType.Libraries.Core.Platform;

public interface IAppInfo
{
    string ProductName { get; }

    Version Version { get; }

    string RepositoryUrl { get; }

    string RepositorySlug { get; }
}
