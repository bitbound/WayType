using WayType.Libraries.Core.Input;
using WayType.Libraries.Native.Linux.NativeInterop;

namespace WayType.Libraries.Native.Linux;

/// <summary>
/// Resolves an XKB keysym by name through libxkbcommon.
/// </summary>
public sealed class XkbKeysymResolver : IKeysymResolver
{
    // A missing or version-mismatched library fails the same way every call, so remember it.
    private static volatile bool unavailable;

    /// <summary>
    /// Looks up the keysym for the given character name, returning false when libxkbcommon is absent.
    /// </summary>
    public bool TryResolve(string character, out uint keysym)
    {
        keysym = 0;

        if (unavailable || string.IsNullOrEmpty(character))
        {
            return false;
        }

        try
        {
            var resolved = LibXkbCommon.xkb_keysym_from_name(character, 0);

            if (resolved == LibXkbCommon.KeysymNotFound)
            {
                return false;
            }

            keysym = resolved;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            unavailable = true;
            return false;
        }
    }
}
