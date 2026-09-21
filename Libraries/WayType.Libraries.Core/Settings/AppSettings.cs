namespace WayType.Libraries.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// How the configured shortcut starts a dictation.
/// </summary>
public enum ShortcutTriggerMode
{
    /// <summary>
    /// One activation starts, the next activation stops.
    /// </summary>
    Tap,

    /// <summary>
    /// Recording follows the physical key, which the portal shortcut API cannot report.
    /// </summary>
    Press,
}

public sealed class AppSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+Space";

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public ShortcutTriggerMode TriggerMode { get; set; } = ShortcutTriggerMode.Tap;

    public string Hotkey { get; set; } = DefaultHotkey;

    public int HistoryItemsToKeep { get; set; } = 50;

    /// <summary>
    /// Keeps the captured WAV next to each history entry so it can be played back.
    /// </summary>
    public bool KeepRecordings { get; set; } = true;

    public int MaximumRecordingSeconds { get; set; } = 120;

    public string? InputDeviceId { get; set; }

    public string? InputDeviceName { get; set; }

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Raises the log level to Debug so audio capture and portal activity are recorded.
    /// </summary>
    public bool DebugLogging { get; set; }

    public SpeechToTextSettings SpeechToText { get; set; } = new();

    public PostProcessingSettings PostProcessing { get; set; } = new();
}
