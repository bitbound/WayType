using System.Runtime.InteropServices;

namespace WayType.Libraries.Native.Linux.NativeInterop;

/// <summary>
/// Bindings for the libpulse-simple record path.
/// </summary>
internal static class LibPulseSimple
{
    private const string Library = "libpulse-simple.so.0";

    // pa_sample_format_t is positional and fixed by libpulse:
    // 0 u8, 1 aLaw, 2 uLaw, 3 s16le, 4 s16be, 5 float32le, 6 float32be, 7 s32le.
    public const int SampleFormatFloat32Le = 5;

    // pa_stream_direction is UNKNOWN=0, PLAYBACK=1, RECORD=2, UPLOAD=3.
    public const int StreamDirectionPlayback = 1;
    public const int StreamDirectionRecord = 2;

    /// <summary>
    /// The value pa_simple_get_latency returns when it cannot measure the stream.
    /// </summary>
    public const ulong LatencyUnavailable = ulong.MaxValue;

    [DllImport(Library, EntryPoint = "pa_simple_new", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_simple_new(
        IntPtr server,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int dir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? dev,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string streamName,
        ref PaSampleSpec ss,
        IntPtr map,
        ref PaBufferAttr attr,
        out int error);

    [DllImport(Library, EntryPoint = "pa_simple_read", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_read(IntPtr s, [Out] byte[] data, nuint bytes, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_write", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_write(IntPtr s, [In] byte[] data, nuint bytes, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_drain", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_simple_drain(IntPtr s, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_get_latency", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong pa_simple_get_latency(IntPtr s, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_simple_free(IntPtr s);
}
