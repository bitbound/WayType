using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Portal;

namespace WayType.Tests;

public class PortalDesktopEntryTests
{
    [Fact]
    public void Build_WhenExecIsAbsolute_WritesAnEntryThePortalCanResolve()
    {
        var contents = DesktopEntryInstaller.Build("WayType", "io.github.bitbound.waytype", "/home/jared/bin/WayType");

        Assert.StartsWith("[Desktop Entry]\n", contents);
        Assert.Contains("Exec=/home/jared/bin/WayType\n", contents);
        Assert.Contains("TryExec=/home/jared/bin/WayType\n", contents);
        Assert.Contains("Name=WayType\n", contents);
        Assert.Contains("NoDisplay=true\n", contents);
    }

    [Fact]
    public void DesktopFilePath_WhenAppIdIsSet_EndsUnderTheApplicationsDirectory()
    {
        var installer = new DesktopEntryInstaller(new FakeAppInfo(), NullLogger.Instance);

        var path = installer.DesktopFilePath;

        Assert.True(Path.IsPathRooted(path), $"Expected an absolute path, got {path}.");
        Assert.EndsWith("io.github.testorg.waytype.desktop", path);
        Assert.EndsWith(Path.Combine("applications", "io.github.testorg.waytype.desktop"), path);
    }
}
