using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Native.Linux.NativeInterop;

namespace WayType.Libraries.Native.Linux;

/// <summary>
/// Captures microphone audio through libpulse-simple, which PipeWire serves on Linux.
/// </summary>
public sealed class PulseAudioRecorder(ILogger<PulseAudioRecorder> logger) : IAudioRecorder, IAudioLevelMeter
{
    private const int SampleRate = 16_000;
    private const int ChannelCount = 1;
    private const int BytesPerSample = sizeof(float);
    private const int ChunkSamples = 1_600;
    private const int ChunkBytes = ChunkSamples * BytesPerSample;
    private const int InitialBufferBytes = 64 * 1024;

    /// <summary>
    /// Ceiling on the audio pulled out of the server buffer at the end of a take. The fragment size
    /// keeps the real figure near one chunk, so this only bounds a server that misreports.
    /// </summary>
    private const int MaxTailBytes = SampleRate * BytesPerSample / 2;

    /// <summary>
    /// A native call the server never answers would otherwise hold its thread forever, so every
    /// capture is capped and reports failure instead of wedging the app.
    /// </summary>
    private static readonly TimeSpan _hangSlack = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Records from the device until endOfRecording fires, then returns what was captured.
    /// A null deviceId lets PulseAudio pick the server default source.
    /// </summary>
    public Task<PcmAudio> RecordAsync(string? deviceId, CancellationToken endOfRecording, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // A dedicated thread keeps a server that stops answering from consuming the pool, which
        // would stall every other Task.Run in the app rather than just this capture.
        var work = Task.Factory.StartNew(
            () => Record(deviceId, endOfRecording, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return AwaitBounded(work, timeout + _hangSlack, cancellationToken);
    }

    private static async Task<PcmAudio> AwaitBounded(Task<PcmAudio> work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"The audio server stopped responding during a {timeout.TotalSeconds:0} second capture. " +
                "Check that PipeWire or PulseAudio is still running.");
        }
    }

    private PcmAudio Record(string? deviceId, CancellationToken endOfRecording, CancellationToken cancellationToken)
    {
        var spec = new PaSampleSpec
        {
            Format = LibPulseSimple.SampleFormatFloat32Le,
            Rate = SampleRate,
            Channels = ChannelCount,
        };

        // libpulse would otherwise leave fragsize at its roughly two second default, and because
        // pa_simple_new passes PA_STREAM_ADJUST_LATENCY that becomes the whole source latency. Asking
        // for one chunk keeps speech arriving as it is spoken instead of lagging behind the stop.
        var attr = CreateRecordBufferAttr();

        var handle = LibPulseSimple.pa_simple_new(
            IntPtr.Zero,
            "WayType",
            LibPulseSimple.StreamDirectionRecord,
            deviceId,
            "WayType dictation",
            ref spec,
            IntPtr.Zero,
            ref attr,
            out var error);

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"pa_simple_new failed for source '{deviceId ?? "<default>"}': {LibPulse.StrError(error)}");
        }

        logger.LogDebug("Opened PulseAudio record stream for source {Source} at {Rate} Hz", deviceId ?? "<default>", SampleRate);

        try
        {
            return ReadLoop(handle, ref spec, endOfRecording, cancellationToken);
        }
        finally
        {
            LibPulseSimple.pa_simple_free(handle);
        }
    }

    // pa_simple_read blocks until it fills the requested chunk and cannot be interrupted directly. A
    // one chunk request with a matching fragment size bounds the stop latency to about 100 ms.
    private PcmAudio ReadLoop(IntPtr handle, ref PaSampleSpec spec, CancellationToken endOfRecording, CancellationToken cancellationToken)
    {
        var buffer = new byte[InitialBufferBytes];
        var length = 0;
        var chunk = new byte[ChunkBytes];

        // A freshly opened record stream first hands back PipeWire's stale pre-open ring buffer,
        // which reads as uninitialized bytes (NaN and out-of-range floats), then switches to live
        // frames. A pa_simple_flush does not reliably clear it, so we read and drop the first chunk.
        if (!endOfRecording.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var primeResult = LibPulseSimple.pa_simple_read(handle, chunk, (nuint)chunk.Length, out var primeError);

            if (primeResult != 0
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

            Append(chunk, chunk.Length, ref buffer, ref length);
            PublishLevel(chunk);
        }

        var tail = DrainTail(handle, ref spec, cancellationToken);
        Append(tail, tail.Length, ref buffer, ref length);

        if (length == 0)
        {
            logger.LogDebug("Capture produced no samples");
            return PcmAudio.Empty;
        }

        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, length)).ToArray();

        logger.LogDebug("Captured {Frames} frames ({Seconds:0.00} s)", samples.Length / ChannelCount, samples.Length / (double)SampleRate);

        return new PcmAudio(samples, SampleRate, ChannelCount);
    }

    /// <summary>
    /// Reads out the audio the server still holds when the take ends. Without this the last fragment
    /// stays in the server buffer and the final words of a take are dropped.
    /// </summary>
    private byte[] DrainTail(IntPtr handle, ref PaSampleSpec spec, CancellationToken cancellationToken)
    {
        var latency = LibPulseSimple.pa_simple_get_latency(handle, out var error);

        // The latency is delivered asynchronously and can be unavailable. A failed measurement only
        // means no tail is recovered, which is the pre-existing behaviour.
        if (latency is LibPulseSimple.LatencyUnavailable or 0)
        {
            logger.LogDebug("No buffered tail to drain (latency unavailable, error {Error}).", error);

            return [];
        }

        var remaining = ComputeTailBytes(latency, ref spec);

        if (remaining <= 0 || cancellationToken.IsCancellationRequested)
        {
            return [];
        }

        var tail = new byte[remaining];
        var filled = 0;

        while (filled < remaining && !cancellationToken.IsCancellationRequested)
        {
            var request = Math.Min(ChunkBytes, remaining - filled);

            if (LibPulseSimple.pa_simple_read(handle, tail, (nuint)request, out var readError) != 0)
            {
                logger.LogDebug("Draining the buffered tail stopped early: {Error}", LibPulse.StrError(readError));
                break;
            }

            filled += request;
        }

        if (filled == 0)
        {
            return [];
        }

        logger.LogDebug("Drained {Bytes} buffered bytes at the end of the take.", filled);

        return filled == tail.Length ? tail : tail[..filled];
    }

    public event EventHandler<float>? LevelChanged;

    public float Level { get; private set; }

    /// <summary>
    /// Reports the peak of a captured frame. The primed frame is deliberately not reported, since it
    /// is PipeWire's stale ring buffer rather than anything the microphone picked up.
    /// </summary>
    private void PublishLevel(byte[] chunk)
    {
        if (LevelChanged is null)
        {
            return;
        }

        Level = ComputePeak(chunk);
        LevelChanged.Invoke(this, Level);
    }

    /// <summary>
    /// Peak amplitude of a little-endian float32 frame, clamped to the valid range.
    /// </summary>
    internal static float ComputePeak(byte[] chunk)
    {
        var samples = MemoryMarshal.Cast<byte, float>(chunk.AsSpan());
        var peak = 0f;

        foreach (var sample in samples)
        {
            // Uninitialized or corrupt frames can carry NaN, which would poison every comparison.
            if (float.IsNaN(sample))
            {
                continue;
            }

            var magnitude = Math.Abs(sample);

            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return Math.Clamp(peak, 0f, 1f);
    }

    private static void Append(byte[] source, int count, ref byte[] buffer, ref int length)    {
        if (count <= 0)
        {
            return;
        }

        if (length + count > buffer.Length)
        {
            var grown = buffer.Length * 2;
            while (grown < length + count)
            {
                grown *= 2;
            }

            Array.Resize(ref buffer, grown);
        }

        Buffer.BlockCopy(source, 0, buffer, length, count);
        length += count;
    }

    /// <summary>
    /// The buffer metrics the capture stream is opened with. Exposed so a test can assert the
    /// fragment size stays small, since a large one is what loses the end of a take.
    /// </summary>
    internal static PaBufferAttr CreateRecordBufferAttr()
    {
        return new PaBufferAttr
        {
            MaxLength = PaBufferAttr.ServerDefault,
            TargetLength = PaBufferAttr.ServerDefault,
            PreBuffer = PaBufferAttr.ServerDefault,
            MinimumRequest = PaBufferAttr.ServerDefault,
            FragmentSize = ChunkBytes,
        };
    }

    /// <summary>
    /// Turns a measured stream latency into the number of bytes to pull out at the end of a take,
    /// capped and aligned down to a whole sample.
    /// </summary>
    internal static int ComputeTailBytes(ulong latencyMicroseconds, ref PaSampleSpec spec)
    {
        var bytes = Math.Min(LibPulse.UsecToBytes(latencyMicroseconds, ref spec), MaxTailBytes);

        return bytes - (bytes % BytesPerSample);
    }
}
