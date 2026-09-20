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
    private readonly ILogger<PortalGlobalHotkeySource> _logger;
    private readonly Task _initializeTask;

    private Connection? _connection;
    private ConnectionInfo? _connectionInfo;
    private IGlobalShortcuts? _proxy;
    private IDisposable? _activatedSubscription;
    private HashSet<string> _ownedShortcutIds = new(StringComparer.Ordinal);
    private bool _disposed;
    private int _supportState;

    public PortalGlobalHotkeySource(IRestoreTokenStore tokens, IPlatformPaths paths, ILogger<PortalGlobalHotkeySource> logger)
    {
        _tokens = tokens;
        _paths = paths;
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

        var storedIds = ReadStoredShortcutIds();
        var shortcutId = storedIds.Count > 0 ? storedIds[0] : PortalRequests.NewToken("waytype_hotkey");

        var sessionHandle = await CreateSessionAsync(cancellationToken);

        if (sessionHandle is null)
        {
            return false;
        }

        var shortcutEntry = new Dictionary<string, object>
        {
            ["description"] = ShortcutDescription,
            ["preferred_trigger"] = trigger,
        };

        var shortcuts = new (string, IDictionary<string, object>)[] { (shortcutId, shortcutEntry) };

        var requestToken = PortalRequests.NewToken("waytype_bind");
        var bindOptions = new Dictionary<string, object> { ["handle_token"] = requestToken };
        var expectedPath = PortalRequests.ExpectedPath(ConnectionInfo, requestToken);

        uint response;
        IDictionary<string, object> results;

        try
        {
            (response, results) = await PortalRequests.AwaitResponseAsync(
                Connection,
                expectedPath,
                () => Proxy.BindShortcutsAsync(new ObjectPath(sessionHandle), shortcuts, string.Empty, bindOptions),
                _userInteractionTimeout,
                cancellationToken);
        }
        catch (DBusException exception)
        {
            // xdg-desktop-portal 1.20+ requires callers to present a valid application id. Do not fall back silently.
            _logger.LogError(
                exception,
                "BindShortcuts failed with D-Bus error {ErrorName}. WayType must present a valid application id (a desktop file and GApplication id) to the portal.",
                exception.ErrorName);

            throw new InvalidOperationException(
                $"The portal rejected the shortcut registration ({exception.ErrorName}): {exception.ErrorMessage}",
                exception);
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
        SaveShortcutIds(assignedIds.Count > 0 ? assignedIds : [shortcutId]);

        _logger.LogInformation("Bound the global hotkey {Trigger} (id {Id}).", trigger, shortcutId);

        return true;
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
            return sessionHandle;
        }

        _logger.LogWarning("GlobalShortcuts CreateSession returned no session handle.");
        return null;
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
        if (_connection is not null)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        Connection? connection = null;
        IDisposable? subscription = null;

        try
        {
            connection = new Connection(Address.Session);
            var connectionInfo = await connection.ConnectAsync().WaitAsync(_connectTimeout, cancellationToken);
            var proxy = connection.CreateProxy<IGlobalShortcuts>(PortalRequests.BusName, new ObjectPath(PortalRequests.ObjectPath));

            // Reading the version confirms the portal actually exposes GlobalShortcuts.
            await proxy.GetAsync<uint>("version");

            subscription = await proxy.WatchActivatedAsync(HandleActivated, HandleSignalError);

            _connectionInfo = connectionInfo;
            _connection = connection;
            _proxy = proxy;
            _activatedSubscription = subscription;
        }
        catch
        {
            subscription?.Dispose();
            connection?.Dispose();
            throw;
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

    private void HandleSignalError(Exception exception)
    {
        _logger.LogWarning(exception, "The Activated signal watcher reported an error.");
    }

    private IReadOnlyList<string> ReadStoredShortcutIds()
    {
        var json = _tokens.Read(_paths.HotkeyRestoreTokenPath);

        if (json is null)
        {
            return [];
        }

        try
        {
            var payload = JsonSerializer.Deserialize<HotkeyRegistration>(json, WayTypeJson.Options);

            return payload?.ShortcutIds is { Length: > 0 } ids ? ids : [];
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring an unreadable hotkey registration file.");
            return [];
        }
    }

    private void SaveShortcutIds(IReadOnlyList<string> shortcutIds)
    {
        var payload = new HotkeyRegistration(shortcutIds.ToArray());
        var json = JsonSerializer.Serialize(payload, WayTypeJson.Options);

        _tokens.Save(_paths.HotkeyRestoreTokenPath, json);
    }

    private void ReleaseConnection()
    {
        _activatedSubscription?.Dispose();
        _activatedSubscription = null;
        _proxy = null;
        _connectionInfo = null;

        if (_connection is not null)
        {
            _connection.Dispose();
            _connection = null;
        }
    }
}
