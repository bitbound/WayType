namespace WayType.Libraries.Portal;

/// <summary>
/// The shortcut ids the portal assigned, persisted so the next launch can reuse the registration.
/// </summary>
internal sealed record HotkeyRegistration(string[] ShortcutIds);
