using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tmds.DBus;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Serialization;

namespace WayType.Libraries.Portal;

/// <summary>
/// Registers the dictation hotkey through the GlobalShortcuts portal and raises Activated only for our shortcuts.
/// </summary>
public sealed class PortalGlobalHotkeySource : IGlobalHotkeySource
{
    private const int SupportUnknown = 0;
    private const int SupportYes = 1;
    private const int SupportNo = 2;
    private const string ShortcutDescription = "WayType voice dictation";

    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _initializeWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _userInteractionTimeout = TimeSpan.FromSeconds(90);

    private readonly IRestoreTokenStore _tokens;
    private readonly IPlatformPaths _paths;
    private readonly IAppInfo _appInfo;
    private readonly ILogger<PortalGlobalHotkeySource> _logger;
    private readonly DesktopEntryInstaller _desktopEntry;
    private readonly Task _initializeTask;

    private Connection? _connection;
    private ConnectionInfo? _connectionInfo;
    private IGlobalShortcuts? _proxy;
    private IDisposable? _activatedSubscription;
    private IDisposable? _deactivatedSubscription;
    private string? _sessionHandle;
    private HashSet<string> _ownedShortcutIds = new(StringComparer.Ordinal);
    private bool _disposed;
    private volatile bool _isConnected;
    private int _supportState;

    public PortalGlobalHotkeySource(
        IRestoreTokenStore tokens,
        IPlatformPaths paths,
        IAppInfo appInfo,
        DesktopEntryInstaller desktopEntry,
        ILogger<PortalGlobalHotkeySource> logger)
    {
        _tokens = tokens;
        _paths = paths;
        _appInfo = appInfo;
        _desktopEntry = desktopEntry;
        _logger = logger;
        _initializeTask = InitializeAsync();
    }

    private Connection Connection => _connection
        ?? throw new InvalidOperationException("The D-Bus connection is not established.");

    private ConnectionInfo ConnectionInfo => _connectionInfo
        ?? throw new InvalidOperationException("The D-Bus connection is not established.");

    private IGlobalShortcuts Proxy => _proxy
        ?? throw new InvalidOperationException("The GlobalShortcuts proxy is not established.");

    public bool IsSupported
    {
        get
        {
            var state = Volatile.Read(ref _supportState);

            if (state == SupportUnknown)
            {
                try
                {
                    _initializeTask.Wait(_initializeWait);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "The portal support probe ended with an error.");
                }

                state = Volatile.Read(ref _supportState);
            }

            return state == SupportYes;
        }
    }

    public event EventHandler? Activated;

    public event EventHandler? Deactivated;

    public event EventHandler? BindingLost;

    public async Task<bool> BindAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string trigger;

        try
        {
            trigger = ShortcutTriggerNormalizer.Normalize(shortcut);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(exception, "The configured hotkey '{Shortcut}' could not be parsed, so it was not bound.", shortcut);
            return false;
        }

        await _initializeTask;

        if (Volatile.Read(ref _supportState) != SupportYes)
        {
            _logger.LogWarning("Global shortcuts are not available on this system, so the hotkey was not bound.");
            return false;
        }

        await EnsureConnectedAsync(cancellationToken);

        var registration = ReadRegistration();
        var hadStoredId = registration?.ShortcutIds.Length > 0;
        var shortcutId = hadStoredId ? registration!.ShortcutIds[0] : PortalRequests.NewToken("waytype_hotkey");

        // A registration written before the trigger was recorded cannot be trusted, so it reconciles too.
        var reassign = hadStoredId && !string.Equals(registration!.Trigger, trigger, StringComparison.Ordinal);

        await CloseSessionAsync();

        var sessionHandle = await CreateSessionAsync(cancellationToken);

        if (sessionHandle is null)
        {
            return false;
        }

        var (response, results) = await BindOnceAsync(sessionHandle, [Entry(shortcutId, trigger)], cancellationToken);

        // Plasma's backend sorts every id into "new" or "returning" against what it loaded for this app id,
        // and a returning id keeps the trigger the daemon already holds: it replies success without a dialog
        // and the new preferred_trigger is dropped. There is no unbind or update method in the portal. The
        // backend does delete any id the app owns but leaves out of a bind, so change the key by binding the
        // id away and then binding it back, in the same session, so the second bind sees it as new.
        if (reassign && response == 0)
        {
            _logger.LogInformation(
                "Reassigning shortcut {Id} from {OldTrigger} to {NewTrigger}.",
                shortcutId,
                registration!.Trigger,
                trigger);

            await BindOnceAsync(sessionHandle, [], cancellationToken);
            (response, results) = await BindOnceAsync(sessionHandle, [Entry(shortcutId, trigger)], cancellationToken);
        }

        if (response == 1)
        {
            _logger.LogInformation("The user cancelled the shortcut dialog.");
            return false;
        }

        if (response != 0)
        {
            _logger.LogWarning("The portal failed to bind the shortcut (response code {Code}).", response);
            return false;
        }

        var assignedIds = PortalRequests.ExtractShortcutIds(results);
        var owned = new HashSet<string>(StringComparer.Ordinal) { shortcutId };

        foreach (var id in assignedIds)
        {
            owned.Add(id);
        }

        Volatile.Write(ref _ownedShortcutIds, owned);
        SaveRegistration(new HotkeyRegistration(assignedIds.Count > 0 ? [.. assignedIds] : [shortcutId], trigger));

        _logger.LogInformation("Bound the global hotkey {Trigger} (id {Id}).", trigger, shortcutId);

        return true;
    }

    private static (string, IDictionary<string, object>) Entry(string shortcutId, string trigger)
    {
        return (shortcutId, new Dictionary<string, object>
        {
            ["description"] = ShortcutDescription,
            ["preferred_trigger"] = trigger,
        });
    }

    private async Task<(uint Response, IDictionary<string, object> Results)> BindOnceAsync(
        string sessionHandle,
        (string, IDictionary<string, object>)[] shortcuts,
        CancellationToken cancellationToken)
    {
        var requestToken = PortalRequests.NewToken("waytype_bind");
        var options = new Dictionary<string, object> { ["handle_token"] = requestToken };
        var expectedPath = PortalRequests.ExpectedPath(ConnectionInfo, requestToken);

        try
        {
            return await PortalRequests.AwaitResponseAsync(
                Connection,
                expectedPath,
                () => Proxy.BindShortcutsAsync(new ObjectPath(sessionHandle), shortcuts, string.Empty, options),
                _userInteractionTimeout,
                cancellationToken);
        }
        catch (DBusException exception)
        {
            _logger.LogError(
                exception,
                "BindShortcuts failed with D-Bus error {ErrorName}.",
                exception.ErrorName);

            throw new InvalidOperationException(
                $"The portal rejected the shortcut registration ({exception.ErrorName}): {exception.ErrorMessage}",
                exception);
        }
    }

    public Task UnbindAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Volatile.Write(ref _ownedShortcutIds, new HashSet<string>(StringComparer.Ordinal));
        _tokens.Delete(_paths.HotkeyRestoreTokenPath);

        // Closing the connection ends the session, which is how the portal releases the binding.
        ReleaseConnection();

        _logger.LogInformation("Unbound the global hotkey and released the session.");

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Volatile.Write(ref _ownedShortcutIds, new HashSet<string>(StringComparer.Ordinal));

        try
        {
            await _initializeTask;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Ignoring the portal initialization result during disposal.");
        }

        ReleaseConnection();
    }

    private async Task<string?> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var requestToken = PortalRequests.NewToken("waytype_gs_session");
        var options = new Dictionary<string, object>
        {
            ["handle_token"] = requestToken,
            ["session_handle_token"] = PortalRequests.NewToken("waytype_gs_handle"),
        };

        var expectedPath = PortalRequests.ExpectedPath(ConnectionInfo, requestToken);
        var (response, results) = await PortalRequests.AwaitResponseAsync(
            Connection,
            expectedPath,
            () => Proxy.CreateSessionAsync(options),
            _userInteractionTimeout,
            cancellationToken);

        if (response != 0)
        {
            _logger.LogWarning("GlobalShortcuts CreateSession returned code {Code}.", response);
            return null;
        }

        if (results.TryGetValue("session_handle", out var handle) && handle is string sessionHandle)
        {
            _sessionHandle = sessionHandle;

            return sessionHandle;
        }

        _logger.LogWarning("GlobalShortcuts CreateSession returned no session handle.");
        return null;
    }

    // Every live session gets its own copy of the Activated signal, so leaving old sessions open makes one
    // key press toggle dictation once per session. Plasma keeps the shortcut grab after a close, so closing
    // the previous session before binding again is safe.
    private async Task CloseSessionAsync()
    {
        var handle = _sessionHandle;

        if (handle is null)
        {
            return;
        }

        _sessionHandle = null;

        try
        {
            var session = Connection.CreateProxy<ISession>(PortalRequests.BusName, new ObjectPath(handle));

            await session.CloseAsync(new Dictionary<string, object>()).WaitAsync(_connectTimeout, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Closing the previous GlobalShortcuts session did not complete.");
        }
    }

    private async Task RegisterAppIdAsync(Connection connection, CancellationToken cancellationToken)
    {
        var registry = connection.CreateProxy<IHostPortalRegistry>(PortalRequests.BusName, new ObjectPath(PortalRequests.ObjectPath));

        try
        {
            await registry.RegisterAsync(_appInfo.AppId, new Dictionary<string, object>()).WaitAsync(_connectTimeout, cancellationToken);

            _logger.LogInformation("Registered the portal application id {AppId}.", _appInfo.AppId);
        }
        catch (DBusException exception)
        {
            // The registry arrived with portal 1.19.4 and is documented as possibly going away. Without it the
            // id can only come from being launched inside an app-{AppId}-N.scope unit.
            _logger.LogWarning(
                exception,
                "The portal host registry refused the application id {AppId} ({ErrorName}), so global shortcuts may be rejected.",
                _appInfo.AppId,
                exception.ErrorName);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await EnsureConnectedAsync(CancellationToken.None);
            Volatile.Write(ref _supportState, SupportYes);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The GlobalShortcuts portal interface is not available, so global hotkeys are disabled.");
            Volatile.Write(ref _supportState, SupportNo);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        // A dropped bus connection leaves a non-null connection object that can never deliver signals,
        // so the object's presence is not enough to treat the session as live.
        if (_connection is not null && _isConnected)
        {
            return;
        }

        ReleaseConnection();

        ObjectDisposedException.ThrowIf(_disposed, this);

        Connection? connection = null;
        IDisposable? activatedSubscription = null;
        IDisposable? deactivatedSubscription = null;

        try
        {
            connection = new Connection(Address.Session);
            var connectionInfo = await connection.ConnectAsync().WaitAsync(_connectTimeout, cancellationToken);

            // GlobalShortcuts rejects any caller whose app id is empty, and for a host app the id comes from
            // this registration plus a desktop file the portal can find. Both must exist before the first
            // portal method call on the connection.
            _desktopEntry.TryInstall();
            await RegisterAppIdAsync(connection, cancellationToken);

            var proxy = connection.CreateProxy<IGlobalShortcuts>(PortalRequests.BusName, new ObjectPath(PortalRequests.ObjectPath));

            // Reading the version confirms the portal actually exposes GlobalShortcuts.
            await proxy.GetAsync<uint>("version");

            activatedSubscription = await proxy.WatchActivatedAsync(HandleActivated, HandleSignalError);
            deactivatedSubscription = await proxy.WatchDeactivatedAsync(HandleDeactivated, HandleSignalError);

            _connectionInfo = connectionInfo;
            _connection = connection;
            _proxy = proxy;
            _activatedSubscription = activatedSubscription;
            _deactivatedSubscription = deactivatedSubscription;
            _isConnected = true;

            // Tmds.DBus exposes no state property, so the event is the only way to notice a dropped bus.
            connection.StateChanged += OnConnectionStateChanged;
        }
        catch
        {
            activatedSubscription?.Dispose();
            deactivatedSubscription?.Dispose();
            connection?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Watches the bus for a dropped connection. The proxy stops delivering signals the moment the
    /// connection goes away, and nothing else in the app notices, so without this the hotkey silently
    /// stops working until the process is restarted.
    /// </summary>
    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (e.State == ConnectionState.Connected)
        {
            _isConnected = true;
            return;
        }

        _isConnected = false;

        if (e.State == ConnectionState.Disconnected)
        {
            _logger.LogWarning(
                e.DisconnectReason,
                "The D-Bus session connection dropped, so the global hotkey binding was lost.");

            ReleaseConnection();

            BindingLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleActivated((ObjectPath sessionHandle, string shortcutId, ulong timestamp, IDictionary<string, object> options) data)
    {
        if (_disposed)
        {
            return;
        }

        if (Volatile.Read(ref _ownedShortcutIds).Contains(data.shortcutId))
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _logger.LogDebug("Ignored an Activated signal for shortcut {Id}.", data.shortcutId);
        }
    }

    private void HandleDeactivated((ObjectPath sessionHandle, string shortcutId, ulong timestamp, IDictionary<string, object> options) data)
    {
        if (_disposed)
        {
            return;
        }

        if (Volatile.Read(ref _ownedShortcutIds).Contains(data.shortcutId))
        {
            Deactivated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleSignalError(Exception exception)
    {
        _logger.LogWarning(exception, "The Activated signal watcher reported an error.");
    }

    private HotkeyRegistration? ReadRegistration()
    {
        var json = _tokens.Read(_paths.HotkeyRestoreTokenPath);

        if (json is null)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<HotkeyRegistration>(json, WayTypeJson.Options);

            return payload?.ShortcutIds is { Length: > 0 } ? payload : null;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring an unreadable hotkey registration file.");
            return null;
        }
    }

    private void SaveRegistration(HotkeyRegistration registration)
    {
        var json = JsonSerializer.Serialize(registration, WayTypeJson.Options);

        _tokens.Save(_paths.HotkeyRestoreTokenPath, json);
    }

    private void ReleaseConnection()
    {
        _activatedSubscription?.Dispose();
        _activatedSubscription = null;
        _deactivatedSubscription?.Dispose();
        _deactivatedSubscription = null;
        _sessionHandle = null;
        _proxy = null;
        _connectionInfo = null;
        _isConnected = false;

        if (_connection is not null)
        {
            // Detach first so disposing the connection cannot re-enter the loss handler.
            _connection.StateChanged -= OnConnectionStateChanged;
            _connection.Dispose();
            _connection = null;
        }
    }
}
