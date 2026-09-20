using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// Settings portal. Read is deprecated and double-wraps its value; ReadOne returns a single variant.
/// Tmds cannot serialize a{sa{sv}}, so ReadAll is intentionally not declared here.
/// </summary>
[DBusInterface("org.freedesktop.portal.Settings")]
public interface IPortalSettings : IDBusObject
{
    Task<object> ReadAsync(string ns, string key);

    Task<object> ReadOneAsync(string ns, string key);

    Task<IDisposable> WatchSettingChangedAsync(
        Action<(string ns, string key, object value)> handler,
        Action<Exception>? onError = null);
}
