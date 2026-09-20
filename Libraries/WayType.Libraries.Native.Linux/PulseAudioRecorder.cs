using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Native.Linux.NativeInterop;

namespace WayType.Libraries.Native.Linux;

/// <summary>
/// Captures microphone audio through libpulse-simple, which PipeWire serves on Linux.
/// </summary>
public sealed class PulseAudioRecorder(ILogger<PulseAudioRecorder> logger) : IAudioRecorder
{
    private const int SampleRate = 16_000;
    private const int ChannelCount = 1;
    private const int BytesPerSample = sizeof(float);
    private const int ChunkSamples = 1_600;
    private const int ChunkBytes = ChunkSamples * BytesPerSample;
    private const int InitialBufferBytes = 64 * 1024;

    /// <summary>
    /// Records from the device until endOfRecording fires, then returns what was captured.
    /// A null deviceId lets PulseAudio pick the server default source.
    /// </summary>
    public Task<PcmAudio> RecordAsync(string? deviceId, CancellationToken endOfRecording, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Record(deviceId, endOfRecording, cancellationToken), cancellationToken);
    }

    private PcmAudio Record(string? deviceId, CancellationToken endOfRecording, CancellationToken cancellationToken)
    {
        var spec = new LibPulseSimple.PaSampleSpec
        {
            Format = LibPulseSimple.SampleFormatFloat32Le,
            Rate = SampleRate,
            Channels = ChannelCount,
        };

        var handle = LibPulseSimple.pa_simple_new(
            IntPtr.Zero,
            "WayType",
            LibPulseSimple.StreamDirectionRecord,
            deviceId,
            "WayType dictation",
            ref spec,
            IntPtr.Zero,
            IntPtr.Zero,
            out var error);

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"pa_simple_new failed for source '{deviceId ?? "<default>"}': {LibPulse.StrError(error)}");
        }

        logger.LogDebug("Opened PulseAudio record stream for source {Source}", deviceId ?? "<default>");

        try
        {
            return ReadLoop(handle, endOfRecording, cancellationToken);
        }
        finally
        {
            LibPulseSimple.pa_simple_free(handle);
        }
    }

    // pa_simple_read blocks until it fills the requested chunk and cannot be interrupted
    // directly. We request a small fixed chunk and re-check the tokens between reads, which
    // bounds the stop latency to roughly one chunk (~100 ms). A live PipeWire source always
    // feeds frames, so a read never blocks past one chunk when endOfRecording fires.
    private PcmAudio ReadLoop(IntPtr handle, CancellationToken endOfRecording, CancellationToken cancellationToken)
    {
        var buffer = new byte[InitialBufferBytes];
        var length = 0;
        var chunk = new byte[ChunkBytes];

        // A freshly opened record stream first hands back PipeWire's stale pre-open ring buffer,
        // which reads as uninitialized bytes (NaN and out-of-range floats), then switches to live
        // frames. A pa_simple_flush does not reliably clear it, so we read and drop the first chunk.
        if (!endOfRecording.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (LibPulseSimple.pa_simple_read(handle, chunk, (nuint)chunk.Length, out var primeError) != 0
                && !endOfRecording.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"pa_simple_read failed: {LibPulse.StrError(primeError)}");
            }
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (endOfRecording.IsCancellationRequested)
            {
                break;
            }

            var result = LibPulseSimple.pa_simple_read(handle, chunk, (nuint)chunk.Length, out var error);

            if (result != 0)
            {
                if (endOfRecording.IsCancellationRequested || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                throw new InvalidOperationException($"pa_simple_read failed: {LibPulse.StrError(error)}");
            }

            if (length + chunk.Length > buffer.Length)
            {
                var grown = buffer.Length * 2;
                while (grown < length + chunk.Length)
                {
                    grown *= 2;
                }

                Array.Resize(ref buffer, grown);
            }

            Buffer.BlockCopy(chunk, 0, buffer, length, chunk.Length);
            length += chunk.Length;
        }

        if (length == 0)
        {
            logger.LogDebug("Capture produced no samples");
            return PcmAudio.Empty;
        }

        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, length)).ToArray();

        logger.LogDebug("Captured {Frames} frames", samples.Length / ChannelCount);

        return new PcmAudio(samples, SampleRate, ChannelCount);
    }
}
