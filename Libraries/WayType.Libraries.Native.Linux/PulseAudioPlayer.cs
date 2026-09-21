using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Native.Linux.NativeInterop;

namespace WayType.Libraries.Native.Linux;

/// <summary>
/// Plays audio back through libpulse-simple, the same route the recorder uses in reverse.
/// </summary>
public sealed class PulseAudioPlayer(ILogger<PulseAudioPlayer> logger) : IAudioPlayer
{
    // pa_simple_write blocks until the server accepts the data, so a small chunk keeps
    // cancellation responsive without adding noticeable overhead.
    private const int ChunkSamples = 1_600;
    private const int BytesPerSample = sizeof(float);

    /// <summary>
    /// Playback buffer target, which the prebuffer inherits. 200 ms starts playback promptly
    /// without asking the sink for a latency it cannot hold.
    /// </summary>
    private const int TargetBytes = ChunkSamples * BytesPerSample * 2;

    /// <summary>
    /// Grace on top of a clip's own length before a stalled server is treated as a failure.
    /// </summary>
    private static readonly TimeSpan _hangSlack = TimeSpan.FromSeconds(30);

    public Task PlayAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wavBytes);

        // A dedicated thread keeps a stalled server from consuming the pool, which would otherwise
        // stall unrelated work in the app.
        var work = Task.Factory.StartNew(
            () => Play(wavBytes, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return AwaitBounded(work, ClipDuration(wavBytes) + _hangSlack, cancellationToken);
    }

    /// <summary>
    /// Bounds playback by the clip's own length, so a server that stops responding is reported
    /// rather than holding the thread indefinitely.
    /// </summary>
    private static async Task AwaitBounded(Task work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "The audio server stopped responding during playback. Check that PipeWire or PulseAudio is still running.");
        }
    }

    private static TimeSpan ClipDuration(byte[] wavBytes)
    {
        if (WavFile.TryRead(wavBytes) is not { } clip || clip.SampleRate <= 0 || clip.ChannelCount <= 0)
        {
            return TimeSpan.FromSeconds(1);
        }

        var frames = clip.PcmBytes.Length / (2 * clip.ChannelCount);

        return TimeSpan.FromSeconds((double)frames / clip.SampleRate);
    }

    private void Play(byte[] wavBytes, CancellationToken cancellationToken)
    {
        if (WavFile.TryRead(wavBytes) is not { } clip)
        {
            throw new InvalidOperationException("The recording is not 16-bit PCM WAV audio.");
        }

        var samples = PcmProcessor.ToFloat32(clip.PcmBytes);

        var spec = new PaSampleSpec
        {
            Format = LibPulseSimple.SampleFormatFloat32Le,
            Rate = (uint)clip.SampleRate,
            Channels = (byte)clip.ChannelCount,
        };

        // TargetLength also sets the default prebuffer, and libpulse would leave that near two
        // seconds, which stops a short clip from ever reaching the prebuffer and starting. A short
        // target makes playback begin promptly for clips of any length.
        var attr = new PaBufferAttr
        {
            MaxLength = PaBufferAttr.ServerDefault,
            TargetLength = TargetBytes,
            PreBuffer = PaBufferAttr.ServerDefault,
            MinimumRequest = PaBufferAttr.ServerDefault,
            FragmentSize = PaBufferAttr.ServerDefault,
        };

        var handle = LibPulseSimple.pa_simple_new(
            IntPtr.Zero,
            "WayType",
            LibPulseSimple.StreamDirectionPlayback,
            null,
            "WayType playback",
            ref spec,
            IntPtr.Zero,
            ref attr,
            out var error);

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"pa_simple_new failed for playback: {LibPulse.StrError(error)}");
        }

        logger.LogDebug("Opened PulseAudio playback stream at {Rate} Hz with {Channels} channel(s)", clip.SampleRate, clip.ChannelCount);

        try
        {
            WriteLoop(handle, samples, cancellationToken);

            // Without a drain the stream is freed while the server still holds buffered audio,
            // which cuts off the tail of the recording.
            if (LibPulseSimple.pa_simple_drain(handle, out var drainError) != 0)
            {
                throw new InvalidOperationException($"pa_simple_drain failed: {LibPulse.StrError(drainError)}");
            }
        }
        finally
        {
            LibPulseSimple.pa_simple_free(handle);
        }
    }

    private static void WriteLoop(IntPtr handle, float[] samples, CancellationToken cancellationToken)
    {
        var pcm = MemoryMarshal.AsBytes(samples.AsSpan());
        var chunkLength = ChunkSamples * BytesPerSample;
        var chunk = new byte[Math.Min(chunkLength, Math.Max(pcm.Length, 1))];
        var offset = 0;

        while (offset < pcm.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var length = Math.Min(chunk.Length, pcm.Length - offset);

            pcm.Slice(offset, length).CopyTo(chunk);

            if (LibPulseSimple.pa_simple_write(handle, chunk, (nuint)length, out var error) != 0)
            {
                throw new InvalidOperationException($"pa_simple_write failed: {LibPulse.StrError(error)}");
            }

            offset += length;
        }
    }
}
