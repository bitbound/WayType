using System.Text.Json.Serialization;

namespace WayType.Libraries.Portal;

/// <summary>
/// The portal's shortcut registration, persisted so the next launch reactivates the same entry.
/// </summary>
/// <param name="ShortcutIds">The shortcut ids the portal assigned.</param>
/// <param name="Trigger">The normalized trigger those ids were last bound to.</param>
internal sealed record HotkeyRegistration(
    [property: JsonPropertyName("shortcutIds")] string[] ShortcutIds,
    [property: JsonPropertyName("trigger")] string? Trigger);
