namespace WayType.Libraries.Core.Audio;

/// <summary>
/// Interleaved PCM in the device's native rate and channel count.
/// </summary>
public sealed record PcmAudio(float[] InterleavedSamples, int SampleRate, int ChannelCount)
{
    public static readonly PcmAudio Empty = new([], 0, 0);

    public int FrameCount => ChannelCount == 0 ? 0 : InterleavedSamples.Length / ChannelCount;
}
