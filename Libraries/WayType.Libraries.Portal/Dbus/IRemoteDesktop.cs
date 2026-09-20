using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// Remote desktop keyboard injection. There is no type-this-text call, so characters are mapped to key events here.
/// </summary>
[DBusInterface("org.freedesktop.portal.RemoteDesktop")]
public interface IRemoteDesktop : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    Task<ObjectPath> SelectDevicesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);

    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);

    Task NotifyKeyboardKeycodeAsync(ObjectPath sessionHandle, IDictionary<string, object> options, int keycode, uint state);

    Task NotifyKeyboardKeysymAsync(ObjectPath sessionHandle, IDictionary<string, object> options, int keysym, uint state);

    Task<T> GetAsync<T>(string prop);
}
