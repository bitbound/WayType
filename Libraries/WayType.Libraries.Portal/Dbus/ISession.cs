using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// A portal session object. Closing it is what stops the portal delivering that session's signals.
/// </summary>
[DBusInterface("org.freedesktop.portal.Session")]
public interface ISession : IDBusObject
{
    Task CloseAsync(IDictionary<string, object> options);
}
