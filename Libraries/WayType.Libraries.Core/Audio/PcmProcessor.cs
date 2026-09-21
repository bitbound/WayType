namespace WayType.Libraries.Core.Audio;

/// <summary>
/// Converts captured device audio into the 16 kHz mono PCM/WAV shape speech-to-text endpoints expect.
/// </summary>
public static class PcmProcessor
{
    public const int TargetSampleRate = 16_000;

    public static float[] ToMono(float[] interleavedSamples, int channelCount)
    {
        if (channelCount <= 1)
        {
            return interleavedSamples;
        }

        var frameCount = interleavedSamples.Length / channelCount;
        var mono = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0f;

            for (var channel = 0; channel < channelCount; channel++)
            {
                sum += interleavedSamples[(frame * channelCount) + channel];
            }

            mono[frame] = sum / channelCount;
        }

        return mono;
    }

    public static float[] Resample(float[] samples, int sourceSampleRate, int targetSampleRate)
    {
        if (sourceSampleRate <= 0 || targetSampleRate <= 0 || samples.Length == 0)
        {
            return [];
        }

        if (sourceSampleRate == targetSampleRate)
        {
            return samples;
        }

        var ratio = (double)targetSampleRate / sourceSampleRate;
        var targetLength = (int)Math.Round(samples.Length * ratio);
        var result = new float[Math.Max(targetLength, 0)];

        for (var index = 0; index < result.Length; index++)
        {
            var sourcePosition = index / ratio;
            var lower = (int)Math.Floor(sourcePosition);
            var upper = Math.Min(lower + 1, samples.Length - 1);

            if (lower >= samples.Length)
            {
                lower = samples.Length - 1;
            }

            var fraction = sourcePosition - lower;
            result[index] = (float)((samples[lower] * (1 - fraction)) + (samples[upper] * fraction));
        }

        return result;
    }

    public static byte[] ToSigned16Bit(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];

        for (var index = 0; index < samples.Length; index++)
        {
            var clamped = Math.Clamp(samples[index], -1f, 1f);
            var value = (short)(clamped * short.MaxValue);

            bytes[(index * 2)] = (byte)(value & 0xFF);
            bytes[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// Widens little-endian signed 16-bit PCM to the float range the audio server takes.
    /// </summary>
    public static float[] ToFloat32(byte[] pcm16Bit)
    {
        ArgumentNullException.ThrowIfNull(pcm16Bit);

        var sampleCount = pcm16Bit.Length / 2;
        var samples = new float[sampleCount];

        for (var index = 0; index < sampleCount; index++)
        {
            var value = (short)(pcm16Bit[index * 2] | (pcm16Bit[(index * 2) + 1] << 8));
            samples[index] = Math.Clamp(value / (float)short.MaxValue, -1f, 1f);
        }

        return samples;
    }

    public static byte[] ToMono16BitPcm(PcmAudio audio, int targetSampleRate = TargetSampleRate)
    {
        if (audio.ChannelCount == 0 || audio.InterleavedSamples.Length == 0)
        {
            return [];
        }

        var mono = ToMono(audio.InterleavedSamples, audio.ChannelCount);
        var resampled = Resample(mono, audio.SampleRate, targetSampleRate);

        return ToSigned16Bit(resampled);
    }

    public static byte[] WriteWav(byte[] pcm16BitMono, int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var dataSize = pcm16BitMono.Length;
        var byteRate = sampleRate * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(pcm16BitMono);
        writer.Flush();

        return stream.ToArray();
    }

    public static byte[] ToWav(PcmAudio audio, int targetSampleRate = TargetSampleRate)
    {
        return WriteWav(ToMono16BitPcm(audio, targetSampleRate), targetSampleRate);
    }
}
