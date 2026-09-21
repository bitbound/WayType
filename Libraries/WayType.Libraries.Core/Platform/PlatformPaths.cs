using System.Diagnostics.CodeAnalysis;

namespace WayType.Libraries.Core.Platform;

public sealed class PlatformPaths : IPlatformPaths
{
    private const string AppFolderName = "waytype";

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");

    public string PromptsFilePath => Path.Combine(ConfigDirectory, "prompts.json");

    public string HistoryFilePath => Path.Combine(DataDirectory, "history.json");

    public string AudioDirectory => Path.Combine(DataDirectory, "audio");

    public string RemoteDesktopRestoreTokenPath => Path.Combine(ConfigDirectory, "remotedesktop-restore-token");

    public string HotkeyRestoreTokenPath => Path.Combine(ConfigDirectory, "hotkey-registration.json");

    public string UpdateStagingDirectory => Path.Combine(Path.GetTempPath(), AppFolderName, "update");

    public PlatformPaths(IEnvironmentVariables? environment = null)
    {
        var env = environment ?? new ProcessEnvironmentVariables();

        var configRoot = FirstNonEmpty(
            env.Get("XDG_CONFIG_HOME"),
            Path.Combine(HomeDirectory(env), ".config"));

        var dataRoot = FirstNonEmpty(
            env.Get("XDG_DATA_HOME"),
            Path.Combine(HomeDirectory(env), ".local", "share"));

        ConfigDirectory = Path.Combine(configRoot, AppFolderName);
        DataDirectory = Path.Combine(dataRoot, AppFolderName);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AudioDirectory);
    }

    private static string HomeDirectory(IEnvironmentVariables env)
    {
        return FirstNonEmpty(env.Get("HOME"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to resolve a home directory for WayType data.");
    }
}

[ExcludeFromCodeCoverage]
public sealed class ProcessEnvironmentVariables : IEnvironmentVariables
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}

public interface IEnvironmentVariables
{
    string? Get(string name);
}
