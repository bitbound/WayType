using WayType.Libraries.Core.Audio;
using WayType.Libraries.Native.Linux;
using WayType.Services;

namespace WayType.Tests;

public class SnackbarServiceTests
{
    private readonly SnackbarService _snackbars = new();

    [Fact]
    public void Show_RaisesTheMessageWithItsKind()
    {
        SnackbarMessage? received = null;
        _snackbars.Shown += (_, message) => received = message;

        _snackbars.Show("Copied.", SnackbarKind.Success);

        Assert.NotNull(received);
        Assert.Equal("Copied.", received.Text);
        Assert.Equal(SnackbarKind.Success, received.Kind);
    }

    [Fact]
    public void Show_WithNoSubscriber_DoesNotThrow()
    {
        // Messages raised before the host is attached are dropped rather than replayed.
        _snackbars.Show("Nobody is listening.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Show_WithBlankText_IsIgnored(string? text)
    {
        var raised = 0;
        _snackbars.Shown += (_, _) => raised++;

        _snackbars.Show(text!);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Show_ForAnError_StaysUpLongerThanAnInfoMessage()
    {
        SnackbarMessage? failure = null;
        SnackbarMessage? notice = null;

        _snackbars.Shown += (_, message) =>
        {
            if (message.Kind == SnackbarKind.Error)
            {
                failure = message;
            }
            else
            {
                notice = message;
            }
        };

        _snackbars.Show("Broke.", SnackbarKind.Error);
        _snackbars.Show("Fine.");

        Assert.NotNull(failure);
        Assert.NotNull(notice);
        Assert.True(failure.Duration > notice.Duration);
    }
}

public class AudioLevelMeterTests
{
    [Fact]
    public void ComputePeak_ReturnsTheLargestMagnitude()
    {
        Assert.Equal(0.75f, PulseAudioRecorder.ComputePeak(Bytes(0.1f, -0.75f, 0.5f)));
    }

    [Fact]
    public void ComputePeak_WithSilence_ReturnsZero()
    {
        Assert.Equal(0f, PulseAudioRecorder.ComputePeak(Bytes(0f, 0f, 0f)));
    }

    [Fact]
    public void ComputePeak_IgnoresNaN()
    {
        // The stale pre-open ring buffer reads as NaN, and a NaN would otherwise win the comparison
        // and pin the meter at whatever it was.
        Assert.Equal(0.5f, PulseAudioRecorder.ComputePeak(Bytes(float.NaN, 0.5f)));
    }

    [Fact]
    public void ComputePeak_WithOnlyNaN_ReturnsZero()
    {
        Assert.Equal(0f, PulseAudioRecorder.ComputePeak(Bytes(float.NaN, float.NaN)));
    }

    [Fact]
    public void ComputePeak_ClampsAboveFullScale()
    {
        Assert.Equal(1f, PulseAudioRecorder.ComputePeak(Bytes(12f)));
    }

    [Fact]
    public void ComputePeak_WithAnEmptyFrame_ReturnsZero()
    {
        Assert.Equal(0f, PulseAudioRecorder.ComputePeak([]));
    }

    [Fact]
    public void Level_StartsAtZero()
    {
        var recorder = new PulseAudioRecorder(Microsoft.Extensions.Logging.Abstractions.NullLogger<PulseAudioRecorder>.Instance);

        Assert.Equal(0f, recorder.Level);
    }

    private static byte[] Bytes(params float[] samples)
    {
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
    }
}
