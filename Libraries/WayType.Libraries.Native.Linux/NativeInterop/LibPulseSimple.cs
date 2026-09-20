using System.Runtime.InteropServices;

namespace WayType.Libraries.Native.Linux.NativeInterop;

/// <summary>
/// Bindings for the libpulse-simple record path.
/// </summary>
internal static class LibPulseSimple
{
    private const string Library = "libpulse-simple.so.0";

    public const int SampleFormatFloat32Le = 3;

    // pa_stream_direction is UNKNOWN=0, PLAYBACK=1, RECORD=2, UPLOAD=3.
    public const int StreamDirectionRecord = 2;

    /// <summary>
    /// Matches the C pa_sample_spec layout: int format, uint32 rate, uint8 channels.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PaSampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [DllImport(Library, EntryPoint = "pa_simple_new", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_simple_new(
        IntPtr server,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int dir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? dev,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string streamName,
        ref PaSampleSpec ss,
        IntPtr map,
        IntPtr attr,
        out int error);

    [DllImport(Library, EntryPoint = "pa_simple_read", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_read(IntPtr s, [Out] byte[] data, nuint bytes, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_simple_free(IntPtr s);
}
