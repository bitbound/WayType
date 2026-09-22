using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Native.Linux;
using WayType.Libraries.Native.Linux.NativeInterop;

namespace WayType.Tests;

/// <summary>
/// Checks the hand-written pa_sample_format_t values against the real library. A wrong value is
/// still a valid format, so libpulse accepts it and streams a different sample width than the
/// recorder decodes. That surfaces as garbage transcriptions rather than an error.
/// </summary>
public class LibPulseSampleFormatTests
{
    [Fact]
    public void Float32Le_IsAFourByteSampleFormat()
    {
        if (!NativeLibrary.TryLoad("libpulse.so.0", out var handle))
        {
            Assert.Skip("libpulse is not installed.");
        }

        var symbol = NativeLibrary.GetExport(handle, "pa_sample_size_of_format");
        var sampleSizeOfFormat = Marshal.GetDelegateForFunctionPointer<SampleSizeOfFormat>(symbol);

        Assert.Equal(4, (int)sampleSizeOfFormat(LibPulseSimple.SampleFormatFloat32Le));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint SampleSizeOfFormat(int format);
}

/// <summary>
/// Exercises the real playback bindings. The sample format constants are hand-written, and a wrong
/// one still opens a stream, so only a real server round trip catches it.
/// </summary>
public class PulseAudioPlayerTests
{
    [SkipOnCiFact]
    public async Task PlayAsync_WithASilentClip_OpensWritesAndDrains()
    {
        if (!NativeLibrary.TryLoad("libpulse-simple.so.0", out _))
        {
            Assert.Skip("libpulse-simple is not installed.");
        }

        // Silence, so the round trip is audible as nothing.
        var wav = PcmProcessor.ToWav(new PcmAudio(new float[1_600], 16_000, 1));
        var player = new PulseAudioPlayer(NullLogger<PulseAudioPlayer>.Instance);

        try
        {
            await player.PlayAsync(wav, TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("pa_simple_new", StringComparison.Ordinal))
        {
            Assert.Skip($"No PulseAudio or PipeWire server is reachable: {ex.Message}");
        }
    }
}

/// <summary>
/// The capture stream has to be opened with a small fragment size. libpulse defaults it to roughly
/// two seconds and pa_simple_new sets PA_STREAM_ADJUST_LATENCY, so the default becomes the whole
/// source latency and the end of a take is still sitting in the server buffer when recording stops.
/// </summary>
public class PulseAudioRecorderBufferTests
{
    [Fact]
    public void CreateRecordBufferAttr_AsksForAOneHundredMillisecondFragment()
    {
        // 1600 frames at 16 kHz, four bytes per mono float sample.
        Assert.Equal(6_400, (int)PulseAudioRecorder.CreateRecordBufferAttr().FragmentSize);
    }

    [Fact]
    public void CreateRecordBufferAttr_LeavesEveryOtherMetricToTheServer()
    {
        var attr = PulseAudioRecorder.CreateRecordBufferAttr();
        uint[] others = [attr.MaxLength, attr.TargetLength, attr.PreBuffer, attr.MinimumRequest];

        Assert.DoesNotContain(others, value => value != PaBufferAttr.ServerDefault);
    }

    [Fact]
    public void CreateRecordBufferAttr_FragmentIsAWholeNumberOfFrames()
    {
        // A fragment that does not divide into frames would make the server split a sample across
        // two deliveries.
        const int bytesPerFrame = sizeof(float);

        Assert.Equal(0, (int)(PulseAudioRecorder.CreateRecordBufferAttr().FragmentSize % bytesPerFrame));
    }

    [SkipOnCiFact]
    public void ComputeTailBytes_WithATwoSecondLatency_IsCappedAtHalfASecond()
    {
        // The cap exists so a server that misreports latency cannot stall the end of a take.
        var spec = Spec();

        Assert.Equal(32_000, PulseAudioRecorder.ComputeTailBytes(2_000_000, ref spec));
    }

    [SkipOnCiFact]
    public void ComputeTailBytes_WithAOneHundredMillisecondLatency_ReturnsOneChunk()
    {
        var spec = Spec();

        Assert.Equal(6_400, PulseAudioRecorder.ComputeTailBytes(100_000, ref spec));
    }

    [SkipOnCiFact]
    public void ComputeTailBytes_AlignsDownToAWholeSample()
    {
        // 150 us at 64 000 bytes per second is 9.6 bytes, which has to become two whole 4 byte
        // samples. A partial sample would shift every later frame.
        var spec = Spec();

        Assert.Equal(8, PulseAudioRecorder.ComputeTailBytes(150, ref spec));
    }

    private static PaSampleSpec Spec()
    {
        return new PaSampleSpec
        {
            Format = LibPulseSimple.SampleFormatFloat32Le,
            Rate = 16_000,
            Channels = 1,
        };
    }
}
