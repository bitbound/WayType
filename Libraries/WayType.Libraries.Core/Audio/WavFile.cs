using System.Buffers.Binary;

namespace WayType.Libraries.Core.Audio;

/// <summary>
/// A WAV file flattened to its PCM payload and the format needed to play it back.
/// </summary>
public sealed record WavClip(byte[] PcmBytes, int SampleRate, int ChannelCount, int BitsPerSample);

/// <summary>
/// Reads the 16-bit PCM WAV files WayType writes. Anything else is rejected, since the player
/// hands the payload straight to the audio server.
/// </summary>
public static class WavFile
{
    private const short PcmFormatTag = 1;
    private const int HeaderLength = 12;
    private const int ChunkHeaderLength = 8;
    private const int MinimumFormatChunkLength = 16;

    public static WavClip? TryRead(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < HeaderLength)
        {
            return null;
        }

        if (!HasTag(bytes, 0, "RIFF"u8) || !HasTag(bytes, 8, "WAVE"u8))
        {
            return null;
        }

        var sampleRate = 0;
        var channelCount = 0;
        var bitsPerSample = 0;
        var isPcm = false;
        var dataOffset = -1;
        var dataLength = 0;

        var offset = HeaderLength;

        while (offset + ChunkHeaderLength <= bytes.Length)
        {
            var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));

            if (declaredLength < 0)
            {
                return null;
            }

            var body = offset + ChunkHeaderLength;
            var length = Math.Min(declaredLength, Math.Max(0, bytes.Length - body));

            if (HasTag(bytes, offset, "fmt "u8) && length >= MinimumFormatChunkLength)
            {
                isPcm = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body)) == PcmFormatTag;
                channelCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 14));
            }
            else if (HasTag(bytes, offset, "data"u8))
            {
                dataOffset = body;
                dataLength = length;
            }

            // Chunks sit on an even byte boundary.
            offset = body + length + (length % 2);
        }

        if (!isPcm || dataOffset < 0 || dataLength <= 0)
        {
            return null;
        }

        if (sampleRate <= 0 || channelCount <= 0 || bitsPerSample != 16)
        {
            return null;
        }

        return new WavClip(bytes[dataOffset..(dataOffset + dataLength)], sampleRate, channelCount, bitsPerSample);
    }

    private static bool HasTag(byte[] bytes, int offset, ReadOnlySpan<byte> tag)
    {
        return offset + tag.Length <= bytes.Length && tag.SequenceEqual(bytes.AsSpan(offset, tag.Length));
    }
}
