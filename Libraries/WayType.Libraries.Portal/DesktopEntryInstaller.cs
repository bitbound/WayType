using System.Diagnostics;
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
public sealed class DesktopEntryInstaller(
    IAppInfo appInfo,
    IPlatformPaths paths,
    ILogger<DesktopEntryInstaller> logger)
{
    private const string LegacyAppId = "io.github.bitbound.waytype";
    private const string IconResourceName = "WayType.Libraries.Portal.AppIcon.png";

    public string DesktopFilePath => Path.Combine(ApplicationsDirectory(), $"{appInfo.AppId}.desktop");

    public bool TryInstall(bool showInApplicationMenu = false)
    {
        var exec = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(exec) || string.IsNullOrWhiteSpace(appInfo.AppId))
        {
            logger.LogWarning("WayType could not determine its executable path or application id, so no portal desktop entry was written.");
            return false;
        }

        var directory = ApplicationsDirectory();
        var path = Path.Combine(directory, $"{appInfo.AppId}.desktop");

        try
        {
            // Rewritten each launch so Exec keeps pointing at the binary that is running, which matters while
            // the single-file build moves between output directories.
            Directory.CreateDirectory(directory);
            var iconPath = ExtractIcon();
            var contents = Build(
                appInfo.ProductName,
                appInfo.AppId,
                exec,
                showInApplicationMenu || IsVisibleEntry(path),
                iconPath);

            File.WriteAllText(path, contents);
            RemoveLegacyEntry(directory);
            RefreshDesktopDatabase(directory);

            logger.LogInformation("Wrote the portal desktop entry to {Path}.", path);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The portal desktop entry could not be written to {Path}.", path);
            return false;
        }
    }

    internal static string Build(
        string productName,
        string applicationId,
        string execPath,
        bool showInApplicationMenu = false,
        string? iconPath = null)
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
            .AppendLine($"Icon={iconPath ?? "waytype"}")
            .AppendLine("Terminal=false")
            .AppendLine("Categories=Utility;Accessibility;")

            .AppendLine($"NoDisplay={(!showInApplicationMenu).ToString().ToLowerInvariant()}");

        return builder.ToString();
    }

    private static bool IsVisibleEntry(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadLines(path).Any(line => string.Equals(line, "NoDisplay=false", StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? ExtractIcon()
    {
        var iconPath = Path.Combine(paths.ConfigDirectory, "appicon.png");

        try
        {
            Directory.CreateDirectory(paths.ConfigDirectory);
            var resource = typeof(DesktopEntryInstaller).Assembly.GetManifestResourceStream(IconResourceName);

            if (resource is null)
            {
                logger.LogWarning("The embedded WayType application icon could not be found.");
                return null;
            }

            using (resource)
            using (var target = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                resource.CopyTo(target);
            }

            return iconPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The WayType application icon could not be extracted to {Path}.", iconPath);
            return null;
        }
    }

    private void RemoveLegacyEntry(string directory)
    {
        var legacyPath = Path.Combine(directory, $"{LegacyAppId}.desktop");

        try
        {
            File.Delete(legacyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "The legacy desktop entry could not be removed from {Path}.", legacyPath);
        }
    }

    private void RefreshDesktopDatabase(string directory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "update-desktop-database",
                Arguments = $"\"{directory}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(exception, "The desktop entry database could not be refreshed.");
        }
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
