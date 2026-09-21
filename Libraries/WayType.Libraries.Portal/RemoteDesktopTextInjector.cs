using Microsoft.Extensions.Logging;
using Tmds.DBus;
using WayType.Libraries.Core.Input;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Settings;

namespace WayType.Libraries.Portal;

/// <summary>
/// Injects text through the RemoteDesktop portal by mapping characters to key events and reusing a saved restore token.
/// </summary>
public sealed class RemoteDesktopTextInjector(
    IRestoreTokenStore tokens,
    IKeysymResolver keysyms,
    IKeycodeResolver keycodes,
    IPlatformPaths paths,
    ISettingsService settings,
    ILogger<RemoteDesktopTextInjector> logger) : ITextInputInjector
{
    /// <summary>
    /// Bounds on the configured pause. Zero would put the press and release in the same input frame,
    /// which types nothing at all, so the floor is one millisecond.
    /// </summary>
    private const int MinKeyDelayMs = 1;

    private const int MaxKeyDelayMs = 100;

    private const int LeftShiftKeycode = 42;
    private const uint KeyboardAndPointerTypes = 3u;
    private const uint PersistUntilRevoked = 2u;

    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan _userInteractionTimeout = TimeSpan.FromSeconds(90);

    private readonly IRestoreTokenStore _tokens = tokens;
    private readonly IKeysymResolver _keysyms = keysyms;
    private readonly IKeycodeResolver _keycodes = keycodes;
    private readonly IPlatformPaths _paths = paths;
    private readonly ISettingsService _settings = settings;
    private readonly ILogger<RemoteDesktopTextInjector> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Connection? _connection;
    private ConnectionInfo? _connectionInfo;
    private IRemoteDesktop? _proxy;
    private bool _established;
    private string? _sessionHandle;

    private Connection Connection => _connection
        ?? throw new InvalidOperationException("The D-Bus connection is not established.");

    private ConnectionInfo ConnectionInfo => _connectionInfo
        ?? throw new InvalidOperationException("The D-Bus connection is not established.");

    private IRemoteDesktop Proxy => _proxy
        ?? throw new InvalidOperationException("The RemoteDesktop proxy is not established.");

    private string SessionHandle => _sessionHandle
        ?? throw new InvalidOperationException("The RemoteDesktop session has not started.");

    public bool HasSavedGrant => _tokens.Read(_paths.RemoteDesktopRestoreTokenPath) is not null;

    public async Task<bool> ProbeGrantAsync(CancellationToken cancellationToken = default)
    {
        if (_tokens.Read(_paths.RemoteDesktopRestoreTokenPath) is null)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = new CancellationTokenSource(_probeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var started = await EstablishInternalAsync(bypassStoredToken: false, _probeTimeout, linked.Token);

            if (!started)
            {
                _logger.LogInformation("The saved restore token is stale; the grant probe did not start a session.");
            }

            return started;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("The grant probe timed out, which usually means the token is stale.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The grant probe failed.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RequestGrantAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var started = await EstablishInternalAsync(bypassStoredToken: true, _userInteractionTimeout, cancellationToken);

            if (started)
            {
                _logger.LogInformation("The keyboard injection grant was requested and stored.");
            }

            return started;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The keyboard injection grant request failed.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevokeGrantAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _tokens.Delete(_paths.RemoteDesktopRestoreTokenPath);
            TearDownSession();
            _logger.LogInformation("Revoked the keyboard injection grant.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_established || _sessionHandle is null)
            {
                if (!HasSavedGrant)
                {
                    throw new InvalidOperationException("Keyboard injection has not been granted. Request the grant before typing.");
                }

                var started = await EstablishInternalAsync(bypassStoredToken: false, _userInteractionTimeout, cancellationToken);
                if (!started)
                {
                    throw new InvalidOperationException("Keyboard injection could not start with the saved grant. Request the grant again.");
                }
            }

            await TypeCoreAsync(text, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> CreateSessionAsync(TimeSpan interactionTimeout, CancellationToken cancellationToken)
    {
        var requestToken = PortalRequests.NewToken("waytype_rd_session");
        var options = new Dictionary<string, object>
        {
            ["handle_token"] = requestToken,
            ["session_handle_token"] = PortalRequests.NewToken("waytype_rd_handle"),
        };

        var expectedPath = PortalRequests.ExpectedPath(ConnectionInfo, requestToken);
        var (response, results) = await PortalRequests.AwaitResponseAsync(
            Connection,
            expectedPath,
            () => Proxy.CreateSessionAsync(options),
            interactionTimeout,
            cancellationToken);

        if (response != 0)
        {
            _logger.LogWarning("RemoteDesktop CreateSession returned code {Code}.", response);
            return null;
        }

        if (results.TryGetValue("session_handle", out var handle) && handle is string sessionHandle)
        {
            return sessionHandle;
        }

        _logger.LogWarning("RemoteDesktop CreateSession returned no session handle.");
        return null;
    }

    private async Task<bool> EstablishInternalAsync(bool bypassStoredToken, TimeSpan interactionTimeout, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);

        var storedToken = bypassStoredToken ? null : _tokens.Read(_paths.RemoteDesktopRestoreTokenPath);
        var sessionHandle = await CreateSessionAsync(interactionTimeout, cancellationToken);

        if (sessionHandle is null)
        {
            return false;
        }

        var selectToken = PortalRequests.NewToken("waytype_rd_select");
        var selectOptions = new Dictionary<string, object>
        {
            ["handle_token"] = selectToken,
            ["types"] = KeyboardAndPointerTypes,
            ["persist_mode"] = PersistUntilRevoked,
        };

        if (!string.IsNullOrEmpty(storedToken))
        {
            selectOptions["restore_token"] = storedToken;
        }

        var selectPath = PortalRequests.ExpectedPath(ConnectionInfo, selectToken);
        var (selectResponse, _) = await PortalRequests.AwaitResponseAsync(
            Connection,
            selectPath,
            () => Proxy.SelectDevicesAsync(new ObjectPath(sessionHandle), selectOptions),
            interactionTimeout,
            cancellationToken);

        if (selectResponse != 0)
        {
            _logger.LogWarning("RemoteDesktop SelectDevices returned code {Code}.", selectResponse);
            return false;
        }

        var startToken = PortalRequests.NewToken("waytype_rd_start");
        var startOptions = new Dictionary<string, object> { ["handle_token"] = startToken };

        var startPath = PortalRequests.ExpectedPath(ConnectionInfo, startToken);
        var (startResponse, startResults) = await PortalRequests.AwaitResponseAsync(
            Connection,
            startPath,
            () => Proxy.StartAsync(new ObjectPath(sessionHandle), string.Empty, startOptions),
            interactionTimeout,
            cancellationToken);

        if (startResponse != 0)
        {
            _logger.LogWarning("RemoteDesktop Start returned code {Code}; the user may have declined.", startResponse);
            return false;
        }

        // Start rotates the restore token on every call, so persist the new one immediately.
        if (startResults.TryGetValue("restore_token", out var tokenValue)
            && tokenValue is string restoreToken
            && !string.IsNullOrEmpty(restoreToken))
        {
            _tokens.Save(_paths.RemoteDesktopRestoreTokenPath, restoreToken);
        }

        _sessionHandle = sessionHandle;
        _established = true;

        return true;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return;
        }

        var connection = new Connection(Address.Session);
        _connectionInfo = await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        _connection = connection;
        _proxy = connection.CreateProxy<IRemoteDesktop>(PortalRequests.BusName, new ObjectPath(PortalRequests.ObjectPath));
    }

    private async Task TypeCoreAsync(string text, CancellationToken cancellationToken)
    {
        var session = new ObjectPath(SessionHandle);
        var emptyOptions = new Dictionary<string, object>();
        var delay = ResolveDelay(_settings.Current.TypingDelayMs);
        var injected = 0;
        var skipped = 0;

        foreach (var character in text)
        {
            if (_keysyms.TryResolve(character.ToString(), out var keysym))
            {
                await Proxy.NotifyKeyboardKeysymAsync(session, emptyOptions, (int)keysym, 1u);
                await PauseAsync(delay, cancellationToken);
                await Proxy.NotifyKeyboardKeysymAsync(session, emptyOptions, (int)keysym, 0u);
                await PauseAsync(delay, cancellationToken);
                injected++;
                continue;
            }

            if (_keycodes.TryResolve(character, out var keycode, out var needsShift))
            {
                if (needsShift)
                {
                    await Proxy.NotifyKeyboardKeycodeAsync(session, emptyOptions, LeftShiftKeycode, 1u);
                    await PauseAsync(delay, cancellationToken);
                }

                await Proxy.NotifyKeyboardKeycodeAsync(session, emptyOptions, keycode, 1u);
                await PauseAsync(delay, cancellationToken);
                await Proxy.NotifyKeyboardKeycodeAsync(session, emptyOptions, keycode, 0u);
                await PauseAsync(delay, cancellationToken);

                if (needsShift)
                {
                    await Proxy.NotifyKeyboardKeycodeAsync(session, emptyOptions, LeftShiftKeycode, 0u);
                    await PauseAsync(delay, cancellationToken);
                }

                injected++;
                continue;
            }

            skipped++;

            // Warning rather than Debug: a character with no mapping is text that will not appear,
            // and hiding that behind debug logging is how it goes unnoticed.
            _logger.LogWarning("No keysym or keycode mapping for '{Character}'; skipped it.", character);
        }

        // One line per dictation. It records the delay actually in effect and whether anything was
        // dropped for want of a mapping, which separates "nothing typed" from "nothing mapped".
        _logger.LogInformation(
            "Injected {Injected} characters with a {Delay} ms key delay ({Skipped} unmapped).",
            injected,
            delay,
            skipped);
    }

    internal static int ResolveDelay(int configuredDelayMs)
    {
        return Math.Clamp(configuredDelayMs, MinKeyDelayMs, MaxKeyDelayMs);
    }

    private static Task PauseAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, cancellationToken);
    }

    private void TearDownSession()
    {
        _established = false;
        _sessionHandle = null;
        _proxy = null;
        _connectionInfo = null;

        if (_connection is not null)
        {
            _connection.Dispose();
            _connection = null;
        }
    }
}
