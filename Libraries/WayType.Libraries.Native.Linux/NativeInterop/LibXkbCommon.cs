using System.Runtime.InteropServices;

namespace WayType.Libraries.Native.Linux.NativeInterop;

/// <summary>
/// Bindings for libxkbcommon keysym lookup.
/// </summary>
internal static class LibXkbCommon
{
    private const string Library = "libxkbcommon.so.0";

    public const uint KeysymNotFound = 0;

    [DllImport(Library, EntryPoint = "xkb_keysym_from_name", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint xkb_keysym_from_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint flags);
}
