namespace WayType.Libraries.Core.Theming;

public enum ColorSchemePreference
{
    Unset,
    Dark,
    Light,
}

public interface ISystemColorSchemeSource
{
    ColorSchemePreference Current { get; }

    event EventHandler<ColorSchemePreference>? Changed;
}
