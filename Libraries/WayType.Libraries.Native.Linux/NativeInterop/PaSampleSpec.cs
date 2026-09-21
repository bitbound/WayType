using System.Runtime.InteropServices;

namespace WayType.Libraries.Native.Linux.NativeInterop;

/// <summary>
/// Matches the C pa_sample_spec layout. int format, uint32 rate, uint8 channels.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PaSampleSpec
{
    public int Format;
    public uint Rate;
    public byte Channels;
}

/// <summary>
/// Matches the C pa_buffer_attr layout. Five uint32 fields, in order.
/// </summary>
/// <remarks>
/// libpulse defaults the recording fragment size to roughly two seconds, and
/// pa_simple_new passes PA_STREAM_ADJUST_LATENCY, which pins the whole source latency to that
/// value. Audio spoken just before a stop therefore sits in the server buffer and is lost unless
/// the fragment size is set low and the remainder is drained.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PaBufferAttr
{
    public uint MaxLength;
    public uint TargetLength;
    public uint PreBuffer;
    public uint MinimumRequest;
    public uint FragmentSize;

    /// <summary>
    /// The value libpulse reads as "pick something sensible".
    /// </summary>
    public const uint ServerDefault = uint.MaxValue;
}
