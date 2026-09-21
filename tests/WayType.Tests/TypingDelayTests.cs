using WayType.Libraries.Core.Settings;
using WayType.Libraries.Portal;

namespace WayType.Tests;

/// <summary>
/// The key delay is applied between the press and the release of every injected character. Making it
/// too small puts both events in the same compositor input frame, and then no text is delivered at
/// all. That is not a degraded result, it is a total failure, so the bounds matter.
/// </summary>
public class TypingDelayTests
{
    [Fact]
    public void DefaultTypingDelay_IsTwoMilliseconds()
    {
        // Verified working on KWin Wayland through XWayland. A failing injection is far more likely
        // to be an overlay window stealing focus than this value being too low.
        Assert.Equal(2, AppSettings.DefaultTypingDelayMs);
    }

    [Fact]
    public void ResolveDelay_WithTheDefault_PassesItThrough()
    {
        Assert.Equal(AppSettings.DefaultTypingDelayMs, RemoteDesktopTextInjector.ResolveDelay(AppSettings.DefaultTypingDelayMs));
    }

    [Fact]
    public void ResolveDelay_WithZero_ClampsToOne()
    {
        // Zero would remove the pause entirely, which types nothing.
        Assert.Equal(1, RemoteDesktopTextInjector.ResolveDelay(0));
    }

    [Fact]
    public void ResolveDelay_WithANegativeValue_ClampsToOne()
    {
        Assert.Equal(1, RemoteDesktopTextInjector.ResolveDelay(-25));
    }

    [Fact]
    public void ResolveDelay_WithAnAbsurdlyLargeValue_ClampsToOneHundred()
    {
        Assert.Equal(100, RemoteDesktopTextInjector.ResolveDelay(100_000));
    }

    [Fact]
    public void ResolveDelay_WithAValueInRange_IsLeftAlone()
    {
        Assert.Equal(37, RemoteDesktopTextInjector.ResolveDelay(37));
    }

    [Fact]
    public void DefaultTypingDelay_IsWithinTheResolvedRange()
    {
        Assert.Equal(AppSettings.DefaultTypingDelayMs, RemoteDesktopTextInjector.ResolveDelay(AppSettings.DefaultTypingDelayMs));
    }
}
