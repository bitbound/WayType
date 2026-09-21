using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// Lets an unsandboxed app bind its own bus connection to an application id, which the portal has
/// required for global shortcuts since 1.21.
/// </summary>
[DBusInterface("org.freedesktop.host.portal.Registry")]
public interface IHostPortalRegistry : IDBusObject
{
    Task RegisterAsync(string appId, IDictionary<string, object> options);

    Task UnregisterAsync(IDictionary<string, object> options);

    Task<(string app_id, string application_id)[]> GetConnectionsAsync();
}
