using WayType.Libraries.Core.Audio;

namespace WayType.Tests;

public class PcmProcessorTests
{
    [Fact]
    public void ToMono_AveragesEachFrameOfInterleavedStereo()
    {
        var mono = PcmProcessor.ToMono([1f, 3f, 5f, 7f], 2);

        Assert.Equal([2f, 6f], mono);
    }

    [Fact]
    public void ToMono_WithSingleChannel_ReturnsTheSamplesUnchanged()
    {
        var samples = new[] { 0.5f, -0.5f };

        Assert.Same(samples, PcmProcessor.ToMono(samples, 1));
    }

    [Fact]
    public void Resample_DownsamplingByHalf_TakesEveryOtherSample()
    {
        var result = PcmProcessor.Resample([1f, 2f, 3f, 4f], 4_000, 2_000);

        Assert.Equal([1f, 3f], result);
    }

    [Fact]
    public void Resample_UpsamplingByDouble_InterpolatesBetweenSamples()
    {
        var result = PcmProcessor.Resample([0f, 1f], 8_000, 16_000);

        Assert.Equal([0f, 0.5f, 1f, 1f], result);
    }

    [Fact]
    public void Resample_WithEqualRates_ReturnsTheSamplesUnchanged()
    {
        var samples = new[] { 0.25f, 0.75f };

        Assert.Same(samples, PcmProcessor.Resample(samples, 16_000, 16_000));
    }

    [Fact]
    public void ToSigned16Bit_WritesLittleEndianAndClampsOutOfRangeSamples()
    {
        var bytes = PcmProcessor.ToSigned16Bit([1f, -1f, 0f, 2f]);

        Assert.Equal([0xFF, 0x7F, 0x01, 0x80, 0x00, 0x00, 0xFF, 0x7F], bytes);
    }

    [Fact]
    public void ToMono16BitPcm_WithNoSamples_ReturnsEmpty()
    {
        Assert.Empty(PcmProcessor.ToMono16BitPcm(PcmAudio.Empty));
    }

    [Fact]
    public void WriteWav_WritesARiffHeaderForMonoSixteenBitAudio()
    {
        var wav = PcmProcessor.WriteWav([0x11, 0x22], 16_000);

        Assert.Equal(46, wav.Length);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        Assert.Equal("fmt "u8.ToArray(), wav[12..16]);
        Assert.Equal(1, ReadInt16(wav, 20));
        Assert.Equal(1, ReadInt16(wav, 22));
        Assert.Equal(16_000, ReadInt32(wav, 24));
        Assert.Equal(32_000, ReadInt32(wav, 28));
        Assert.Equal(16, ReadInt16(wav, 34));
        Assert.Equal("data"u8.ToArray(), wav[36..40]);
        Assert.Equal(2, ReadInt32(wav, 40));
        Assert.Equal([0x11, 0x22], wav[44..46]);
    }

    [Fact]
    public void ToWav_ResamplesCapturedStereoDownToTheTargetRate()
    {
        var captured = new PcmAudio([0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f], 48_000, 2);

        var wav = PcmProcessor.ToWav(captured);

        // Three 48 kHz stereo frames collapse to one 16 kHz mono frame, so only two PCM bytes follow the header.
        Assert.Equal(46, wav.Length);
        Assert.Equal(16_000, ReadInt32(wav, 24));
    }

    [Fact]
    public void ToWav_WithNoAudio_WritesAHeaderOnlyFile()
    {
        Assert.Equal(44, PcmProcessor.ToWav(PcmAudio.Empty).Length);
    }

    private static int ReadInt16(byte[] bytes, int offset)
    {
        return BitConverter.ToInt16(bytes, offset);
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        return BitConverter.ToInt32(bytes, offset);
    }
}
