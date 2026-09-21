using System.Text;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Platform;

namespace WayType.Libraries.Portal;

/// <summary>
/// Writes the desktop file that backs WayType's portal application id.
/// </summary>
/// <remarks>
/// The portal resolves a host app's id by looking up {app-id}.desktop through GLib, so the file has to
/// exist before the first portal call. Exec is checked against the portal daemon's own PATH, so it has
/// to be an absolute path rather than a bare program name.
/// </remarks>
internal sealed class DesktopEntryInstaller(IAppInfo appInfo, ILogger logger)
{
    public string DesktopFilePath => Path.Combine(ApplicationsDirectory(), $"{appInfo.AppId}.desktop");

    public bool TryInstall()
    {
        var exec = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(exec) || string.IsNullOrWhiteSpace(appInfo.AppId))
        {
            logger.LogWarning("WayType could not determine its executable path or application id, so no portal desktop entry was written.");
            return false;
        }

        var directory = ApplicationsDirectory();
        var path = Path.Combine(directory, $"{appInfo.AppId}.desktop");
        var contents = Build(appInfo.ProductName, appInfo.AppId, exec);

        try
        {
            // Rewritten each launch so Exec keeps pointing at the binary that is running, which matters while
            // the single-file build moves between output directories.
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, contents);

            logger.LogInformation("Wrote the portal desktop entry to {Path}.", path);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The portal desktop entry could not be written to {Path}.", path);
            return false;
        }
    }

    internal static string Build(string productName, string applicationId, string execPath)
    {
        var builder = new StringBuilder();

        builder
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine($"Name={productName}")
            .AppendLine("GenericName=Voice dictation")
            .AppendLine("Comment=Dictate text into any application")
            .AppendLine($"Exec={execPath}")
            .AppendLine($"TryExec={execPath}")
            .AppendLine($"StartupWMClass={applicationId}")
            .AppendLine("Icon=waytype")
            .AppendLine("Terminal=false")
            .AppendLine("Categories=Utility;Accessibility;")

            // This entry exists to give the portal an id, not to add a launcher icon for a binary that
            // moves between build folders during development.
            .AppendLine("NoDisplay=true");

        return builder.ToString();
    }

    private static string ApplicationsDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathRooted(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(dataHome, "applications");
    }
}
