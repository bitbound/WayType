using Microsoft.Extensions.Logging;
using Tmds.DBus;
using WayType.Libraries.Core.Theming;

namespace WayType.Libraries.Portal;

/// <summary>
/// Reads the desktop color-scheme preference from the portal Settings interface and tracks changes.
/// </summary>
public sealed class PortalSystemColorSchemeSource : ISystemColorSchemeSource
{
    private const string AppearanceNamespace = "org.freedesktop.appearance";
    private const string ColorSchemeKey = "color-scheme";

    private static readonly TimeSpan _startupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _readTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger<PortalSystemColorSchemeSource> _logger;

    private Connection? _connection;
    private IDisposable? _subscription;
    private ColorSchemePreference _current = ColorSchemePreference.Unset;

    public PortalSystemColorSchemeSource(ILogger<PortalSystemColorSchemeSource> logger)
    {
        _logger = logger;

        try
        {
            // Run off the current context so this synchronous constructor cannot deadlock a caller with a sync context.
            Task.Run(InitializeAsync).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The portal Settings interface is unreachable; the theme falls back to the app default.");

            if (_subscription is null)
            {
                ReleaseConnection();
            }
        }
    }

    public ColorSchemePreference Current => _current;

    public event EventHandler<ColorSchemePreference>? Changed;

    private async Task InitializeAsync()
    {
        _connection = new Connection(Address.Session);
        await _connection.ConnectAsync().WaitAsync(_startupTimeout);

        var proxy = _connection.CreateProxy<IPortalSettings>(PortalRequests.BusName, new ObjectPath(PortalRequests.ObjectPath));

        _current = await ReadPreferenceAsync(proxy);

        _subscription = await proxy.WatchSettingChangedAsync(HandleSettingChanged, HandleSignalError).WaitAsync(_startupTimeout);
    }

    private async Task<ColorSchemePreference> ReadPreferenceAsync(IPortalSettings proxy)
    {
        var value = await TryReadAsync(() => proxy.ReadOneAsync(AppearanceNamespace, ColorSchemeKey));

        if (value is not null)
        {
            return MapPreference(value);
        }

        var legacy = await TryReadAsync(() => proxy.ReadAsync(AppearanceNamespace, ColorSchemeKey));

        return legacy is not null ? MapPreference(legacy) : ColorSchemePreference.Unset;
    }

    private async Task<object?> TryReadAsync(Func<Task<object>> read)
    {
        try
        {
            return await read().WaitAsync(_readTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "A portal Settings read failed.");
            return null;
        }
    }

    private void HandleSettingChanged((string ns, string key, object value) data)
    {
        if (!string.Equals(data.ns, AppearanceNamespace, StringComparison.Ordinal)
            || !string.Equals(data.key, ColorSchemeKey, StringComparison.Ordinal))
        {
            return;
        }

        var preference = MapPreference(data.value);

        if (preference == _current)
        {
            return;
        }

        _current = preference;
        Changed?.Invoke(this, preference);
    }

    private void HandleSignalError(Exception exception)
    {
        _logger.LogWarning(exception, "The SettingChanged signal watcher reported an error.");
    }

    private static ColorSchemePreference MapPreference(object value)
    {
        return PortalRequests.ToUInt32(value) switch
        {
            1 => ColorSchemePreference.Dark,
            2 => ColorSchemePreference.Light,
            _ => ColorSchemePreference.Unset,
        };
    }

    private void ReleaseConnection()
    {
        _subscription?.Dispose();
        _subscription = null;

        if (_connection is not null)
        {
            _connection.Dispose();
            _connection = null;
        }
    }
}
