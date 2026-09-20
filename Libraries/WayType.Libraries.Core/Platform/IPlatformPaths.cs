namespace WayType.Libraries.Core.Platform;

public interface IPlatformPaths
{
    string ConfigDirectory { get; }

    string DataDirectory { get; }

    string SettingsFilePath { get; }

    string PromptsFilePath { get; }

    string HistoryFilePath { get; }

    string RemoteDesktopRestoreTokenPath { get; }

    string HotkeyRestoreTokenPath { get; }

    string UpdateStagingDirectory { get; }

    void EnsureDirectories();
}
