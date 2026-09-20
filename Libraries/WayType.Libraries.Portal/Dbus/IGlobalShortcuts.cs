using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// Global shortcuts portal (spec v2). There is no ActivateShortcuts or ChangeShortcuts; bindings live on a session.
/// </summary>
[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
public interface IGlobalShortcuts : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    Task<ObjectPath> BindShortcutsAsync(
        ObjectPath sessionHandle,
        (string shortcutId, IDictionary<string, object> options)[] shortcuts,
        string parentWindow,
        IDictionary<string, object> options);

    Task<ObjectPath> ListShortcutsAsync(ObjectPath sessionHandle, IDictionary<string, object> options);

    Task ConfigureShortcutsAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);

    Task<T> GetAsync<T>(string prop);

    Task<IDisposable> WatchActivatedAsync(
        Action<(ObjectPath sessionHandle, string shortcutId, ulong timestamp, IDictionary<string, object> options)> handler,
        Action<Exception>? onError = null);

    Task<IDisposable> WatchDeactivatedAsync(
        Action<(ObjectPath sessionHandle, string shortcutId, ulong timestamp, IDictionary<string, object> options)> handler,
        Action<Exception>? onError = null);

    Task<IDisposable> WatchShortcutsChangedAsync(
        Action<(ObjectPath sessionHandle, (string shortcutId, IDictionary<string, object> options)[] shortcuts)> handler,
        Action<Exception>? onError = null);
}
