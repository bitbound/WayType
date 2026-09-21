using Avalonia.Input;
using WayType.Libraries.Portal;
using WayType.Views;

namespace WayType.Tests;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(Key.Space, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+Space")]
    [InlineData(Key.A, KeyModifiers.Meta, "Super+A")]
    [InlineData(Key.D5, KeyModifiers.Control, "Ctrl+5")]
    [InlineData(Key.Back, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+BackSpace")]
    [InlineData(Key.F13, KeyModifiers.Control, "Ctrl+F13")]
    [InlineData(Key.Tab, KeyModifiers.Control, "Ctrl+Tab")]
    [InlineData(Key.Return, KeyModifiers.Alt, "Alt+Return")]
    [InlineData(Key.Escape, KeyModifiers.Control, "Ctrl+Escape")]
    [InlineData(Key.Delete, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+Delete")]
    [InlineData(Key.PageUp, KeyModifiers.Control, "Ctrl+PageUp")]
    [InlineData(Key.PageDown, KeyModifiers.Control, "Ctrl+PageDown")]
    [InlineData(Key.Up, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+Up")]
    [InlineData(Key.Home, KeyModifiers.None, "Home")]
    public void TryFormat_ReturnsTheSpellingThePortalShortcutWants(Key key, KeyModifiers modifiers, string expected)
    {
        Assert.True(HotkeyKeyFormatter.TryFormat(key, modifiers, out var combo));
        Assert.Equal(expected, combo);
    }

    [Theory]
    [InlineData(Key.None)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.CapsLock)]
    public void IsWaitingForKey_WhileOnlyModifiersAreDown_ReturnsTrue(Key key)
    {
        Assert.True(HotkeyKeyFormatter.IsWaitingForKey(key));
    }

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.A)]
    [InlineData(Key.D5)]
    public void IsWaitingForKey_OnceARealKeyArrives_ReturnsFalse(Key key)
    {
        Assert.False(HotkeyKeyFormatter.IsWaitingForKey(key));
    }

    // A captured shortcut has to be one the portal will register, so nothing the capture UI can
    // produce may fall outside the shortcut vocabulary.
    [Theory]
    [InlineData(Key.Space, KeyModifiers.Control, "Ctrl+Space", "CTRL+Space")]
    [InlineData(Key.A, KeyModifiers.Meta, "Super+A", "LOGO+A")]
    [InlineData(Key.D5, KeyModifiers.Control, "Ctrl+5", "CTRL+5")]
    [InlineData(Key.Back, KeyModifiers.Control, "Ctrl+BackSpace", "CTRL+BackSpace")]
    [InlineData(Key.F24, KeyModifiers.Alt, "Alt+F24", "ALT+F24")]
    [InlineData(Key.Return, KeyModifiers.None, "Return", "Return")]
    [InlineData(Key.PageUp, KeyModifiers.Control, "Ctrl+PageUp", "CTRL+Page_Up")]
    [InlineData(Key.PageDown, KeyModifiers.Control, "Ctrl+PageDown", "CTRL+Page_Down")]
    public void CapturedCombo_IsAcceptedByThePortalShortcutNormalizer(
        Key key,
        KeyModifiers modifiers,
        string expectedCombo,
        string expectedTrigger)
    {
        Assert.True(HotkeyKeyFormatter.TryFormat(key, modifiers, out var combo));
        Assert.Equal(expectedCombo, combo);

        Assert.Equal(expectedTrigger, ShortcutTriggerNormalizer.Normalize(combo));
    }
}
