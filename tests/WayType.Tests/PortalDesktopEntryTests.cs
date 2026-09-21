using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Portal;

namespace WayType.Tests;

public class PortalDesktopEntryTests
{
    [Fact]
    public void Build_WhenExecIsAbsolute_WritesAnEntryThePortalCanResolve()
    {
        var contents = DesktopEntryInstaller.Build("WayType", "org.bitbound.waytype", "/home/jared/bin/WayType");

        Assert.StartsWith("[Desktop Entry]\n", contents);
        Assert.Contains("Exec=/home/jared/bin/WayType\n", contents);
        Assert.Contains("TryExec=/home/jared/bin/WayType\n", contents);
        Assert.Contains("Name=WayType\n", contents);
        Assert.Contains("Icon=waytype\n", contents);
        Assert.Contains("NoDisplay=true\n", contents);
    }

    [Fact]
    public void Build_WhenApplicationMenuShortcutIsRequested_MakesTheEntryVisible()
    {
        var contents = DesktopEntryInstaller.Build("WayType", "org.bitbound.waytype", "/home/jared/bin/WayType", true);

        Assert.Contains("NoDisplay=false\n", contents);
    }

    [Fact]
    public void Build_WhenIconPathIsProvided_UsesTheAbsoluteIconPath()
    {
        var contents = DesktopEntryInstaller.Build(
            "WayType",
            "org.bitbound.waytype",
            "/home/jared/bin/WayType",
            iconPath: "/home/jared/.config/waytype/appicon.png");

        Assert.Contains("Icon=/home/jared/.config/waytype/appicon.png\n", contents);
    }

    [Fact]
    public void DesktopFilePath_WhenAppIdIsSet_EndsUnderTheApplicationsDirectory()
    {
        var installer = new DesktopEntryInstaller(
            new FakeAppInfo(),
            new TestPlatformPaths(),
            NullLogger<DesktopEntryInstaller>.Instance);

        var path = installer.DesktopFilePath;

        Assert.True(Path.IsPathRooted(path), $"Expected an absolute path, got {path}.");
        Assert.EndsWith("io.github.testorg.waytype.desktop", path);
        Assert.EndsWith(Path.Combine("applications", "io.github.testorg.waytype.desktop"), path);
    }
}
