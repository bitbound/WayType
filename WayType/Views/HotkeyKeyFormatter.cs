using Avalonia.Input;

namespace WayType.Views;

// Turns a captured key press into the "Ctrl+Alt+Space" spelling ShortcutTriggerNormalizer accepts,
// so a recorded shortcut is always one the portal will register.
internal static class HotkeyKeyFormatter
{
    // Presses of these keys mean the user is still building the combination.
    private static readonly HashSet<string> _incompleteKeys =
        new(StringComparer.Ordinal)
        {
            "None",
            "LeftCtrl",
            "RightCtrl",
            "LeftShift",
            "RightShift",
            "LeftAlt",
            "RightAlt",
            "LWin",
            "RWin",
            "Capital",
            "CapsLock",
            "NumLock",
            "Scroll",
            "ScrollLock",
        };

    // Avalonia and the freedesktop shortcut spec do not always spell a key the same way. The last three
    // are enum aliases, so which spelling Key.ToString() yields depends on the declaration order.
    private static readonly Dictionary<string, string> _renamedKeys =
        new(StringComparer.Ordinal)
        {
            ["Back"] = "BackSpace",
            ["Enter"] = "Return",
            ["Prior"] = "PageUp",
            ["Next"] = "PageDown",
        };

    private static readonly HashSet<string> _namedKeys =
        new(StringComparer.Ordinal)
        {
            "Space",
            "Tab",
            "Return",
            "Escape",
            "BackSpace",
            "Delete",
            "Insert",
            "Home",
            "End",
            "PageUp",
            "PageDown",
            "Up",
            "Down",
            "Left",
            "Right",
        };

    public static bool IsWaitingForKey(Key key)
    {
        return _incompleteKeys.Contains(key.ToString());
    }

    public static bool TryFormat(Key key, KeyModifiers modifiers, out string combo)
    {
        combo = string.Empty;

        if (!TryGetKeyToken(key, out var keyToken))
        {
            return false;
        }

        var parts = new List<string>();

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Super");
        }

        parts.Add(keyToken);
        combo = string.Join('+', parts);

        return true;
    }

    private static bool TryGetKeyToken(Key key, out string token)
    {
        var name = key.ToString();

        // Key.D5 and friends carry the digit the portal wants, not the enum label.
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
        {
            token = name[1].ToString();
            return true;
        }

        if (_renamedKeys.TryGetValue(name, out var renamed))
        {
            token = renamed;
            return true;
        }

        if (_namedKeys.Contains(name) || IsLetter(name) || IsFunctionKey(name))
        {
            token = name;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private static bool IsLetter(string name)
    {
        return name.Length == 1 && name[0] is >= 'A' and <= 'Z';
    }

    private static bool IsFunctionKey(string name)
    {
        if (name.Length < 2 || name[0] != 'F')
        {
            return false;
        }

        return int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 24;
    }
}
