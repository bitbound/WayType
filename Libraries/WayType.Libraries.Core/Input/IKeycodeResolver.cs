namespace WayType.Libraries.Core.Input;

/// <summary>
/// Maps a character to an evdev keycode as a fallback when keysym injection is unavailable.
/// </summary>
public interface IKeycodeResolver
{
    bool TryResolve(char character, out int keycode, out bool needsShift);
}
