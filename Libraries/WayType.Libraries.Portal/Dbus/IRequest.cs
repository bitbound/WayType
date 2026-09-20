using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// The per-request object the portal hands back. Real results arrive on its Response signal, not the method return.
/// </summary>
[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(
        Action<(uint response, IDictionary<string, object> results)> handler,
        Action<Exception>? onError = null);

    Task CloseAsync();
}
