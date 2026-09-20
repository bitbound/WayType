namespace WayType.Libraries.Portal;

/// <summary>
/// Turns user-friendly shortcuts like "Ctrl+Alt+Space" into the freedesktop Shortcuts spelling the portal expects.
/// </summary>
internal static class ShortcutTriggerNormalizer
{
    private static readonly Dictionary<string, string> _modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = "CTRL",
        ["control"] = "CTRL",
        ["alt"] = "ALT",
        ["option"] = "ALT",
        ["shift"] = "SHIFT",
        ["num"] = "NUM",
        ["super"] = "LOGO",
        ["meta"] = "LOGO",
        ["logo"] = "LOGO",
        ["win"] = "LOGO",
        ["cmd"] = "LOGO",
        ["command"] = "LOGO",
    };

    private static readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "Space",
        ["tab"] = "Tab",
        ["return"] = "Return",
        ["enter"] = "Return",
        ["escape"] = "Escape",
        ["esc"] = "Escape",
        ["backspace"] = "BackSpace",
        ["delete"] = "Delete",
        ["del"] = "Delete",
        ["insert"] = "Insert",
        ["ins"] = "Insert",
        ["home"] = "Home",
        ["end"] = "End",
        ["pageup"] = "Page_Up",
        ["page_up"] = "Page_Up",
        ["pagedown"] = "Page_Down",
        ["page_down"] = "Page_Down",
        ["up"] = "Up",
        ["down"] = "Down",
        ["left"] = "Left",
        ["right"] = "Right",
    };

    public static string Normalize(string trigger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        var tokens = trigger.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            throw new FormatException($"The shortcut '{trigger}' does not name any key.");
        }

        var parts = new string[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            parts[i] = NormalizeToken(tokens[i], trigger);
        }

        return string.Join('+', parts);
    }

    private static string NormalizeToken(string token, string trigger)
    {
        if (_modifiers.TryGetValue(token, out var modifier))
        {
            return modifier;
        }

        if (_keys.TryGetValue(token, out var key))
        {
            return key;
        }

        if (token.Length == 1 && char.IsAsciiLetter(token[0]))
        {
            return char.ToUpperInvariant(token[0]).ToString();
        }

        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            return token;
        }

        if (IsFunctionKey(token))
        {
            return token.ToUpperInvariant();
        }

        throw new FormatException($"The shortcut '{trigger}' uses the unsupported key '{token}'.");
    }

    private static bool IsFunctionKey(string token)
    {
        if (token.Length < 2 || (token[0] != 'f' && token[0] != 'F'))
        {
            return false;
        }

        var numberPart = token.AsSpan(1);

        if (!int.TryParse(numberPart, out var number))
        {
            return false;
        }

        return number is >= 1 and <= 24;
    }
}
