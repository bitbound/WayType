using System.Reflection;

namespace WayType.Libraries.Core.Platform;

public sealed class AssemblyAppInfo : IAppInfo
{
    private const string RepoUrl = "https://github.com/bitbound/WayType";

    public string ProductName => "WayType";

    public Version Version => GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public string RepositoryUrl => RepoUrl;

    public string RepositorySlug => "bitbound/WayType";

    public static Version ReadVersionFrom(Assembly assembly)
    {
        return assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }
}
