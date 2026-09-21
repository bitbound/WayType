using WayType.Libraries.Native.Linux;

namespace WayType.Tests;

/// <summary>
/// The keycodes here are Linux evdev input event codes, the same values the RemoteDesktop portal
/// takes. Getting one wrong does not throw: the portal happily injects whatever key that code names,
/// so a bad entry shows up as the wrong character or as nothing at all.
/// </summary>
public class EvdevKeycodeResolverTests
{
    // From linux/input-event-codes.h.
    private const int KeyTab = 15;
    private const int KeyQ = 16;
    private const int KeyEnter = 28;
    private const int KeyA = 30;
    private const int KeyJ = 36;
    private const int KeySpace = 57;
    private const int KeyKp0 = 82;

    private readonly EvdevKeycodeResolver _resolver = new();

    [Theory]
    [InlineData(' ', KeySpace)]
    [InlineData('\n', KeyEnter)]
    [InlineData('\t', KeyTab)]
    public void TryResolve_ForControlCharacters_ReturnsTheEvdevCode(char character, int expected)
    {
        Assert.True(_resolver.TryResolve(character, out var keycode, out var needsShift));
        Assert.Equal(expected, keycode);
        Assert.False(needsShift);
    }

    [Fact]
    public void TryResolve_ForSpace_DoesNotResolveToTheNumpadZeroKey()
    {
        // Numpad zero is what a mis-numbered space resolves to, and it types nothing when numlock
        // is off, which is exactly how the bug hid.
        _resolver.TryResolve(' ', out var keycode, out _);

        Assert.NotEqual(KeyKp0, keycode);
    }

    [Fact]
    public void TryResolve_ForNewline_DoesNotResolveToTheSameKeyAsJ()
    {
        // Enter was once mapped to 36, which is the j key, so newlines came out as the letter j.
        _resolver.TryResolve('\n', out var enter, out _);
        _resolver.TryResolve('j', out var letter, out _);

        Assert.NotEqual(letter, enter);
    }

    [Theory]
    [InlineData('q', KeyQ, false)]
    [InlineData('a', KeyA, false)]
    [InlineData('j', KeyJ, false)]
    [InlineData('Q', KeyQ, true)]
    [InlineData('!', 2, true)]
    public void TryResolve_ForLettersAndPunctuation_ReturnsTheEvdevCodeAndShiftState(char character, int expected, bool expectedShift)
    {
        Assert.True(_resolver.TryResolve(character, out var keycode, out var needsShift));
        Assert.Equal(expected, keycode);
        Assert.Equal(expectedShift, needsShift);
    }

    [Fact]
    public void TryResolve_WithAnUnmappedCharacter_ReturnsFalse()
    {
        Assert.False(_resolver.TryResolve('é', out var keycode, out var needsShift));
        Assert.Equal(0, keycode);
        Assert.False(needsShift);
    }

    [Fact]
    public void TryResolve_ForEveryMappedCharacter_UsesAValidKeycode()
    {
        // Every entry has to be a real evdev code. The highest key this map uses is on the bottom
        // letter row, and nothing should be zero or negative.
        var printable = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789`-=[]\\;',./ ~!@#$%^&*()_+{}|:\"<>?";

        foreach (var character in printable)
        {
            Assert.True(_resolver.TryResolve(character, out var keycode, out _), $"'{character}' has no keycode mapping.");
            Assert.InRange(keycode, 1, 255);
        }
    }
}
