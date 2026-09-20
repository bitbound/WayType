using Avalonia;
using Avalonia.Styling;
using WayType.Libraries.Core.Settings;
using WayType.Libraries.Core.Theming;

namespace WayType.Services;

public interface IThemeProvider
{
    ThemeVariant CurrentTheme { get; }

    event EventHandler? ThemeChanged;

    void Apply();
}

/// <summary>
/// Maps the stored theme mode onto Avalonia's RequestedThemeVariant, resolving System through the portal.
/// </summary>
public sealed class ThemeProvider : IThemeProvider
{
    private readonly ISystemColorSchemeSource _systemScheme;
    private readonly ISettingsService _settings;

    public ThemeProvider(ISettingsService settings, ISystemColorSchemeSource systemScheme)
    {
        _settings = settings;
        _systemScheme = systemScheme;

        _settings.SettingsChanged += (_, _) => NotifyChanged();
        _systemScheme.Changed += (_, _) => NotifyChanged();
    }

    public ThemeVariant CurrentTheme
    {
        get
        {
            return _settings.Current.Theme switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => _systemScheme.Current switch
                {
                    ColorSchemePreference.Dark => ThemeVariant.Dark,
                    ColorSchemePreference.Light => ThemeVariant.Light,
                    _ => ThemeVariant.Default,
                },
            };
        }
    }

    public event EventHandler? ThemeChanged;

    public void Apply()
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = CurrentTheme;
        }
    }

    private void NotifyChanged()
    {
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        Apply();
    }
}
