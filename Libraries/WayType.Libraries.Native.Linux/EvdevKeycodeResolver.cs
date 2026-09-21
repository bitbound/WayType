using WayType.Libraries.Core.Input;

namespace WayType.Libraries.Native.Linux;

/// <summary>
/// Maps a character to an evdev keycode plus a shift flag for the US layout.
/// </summary>
public sealed class EvdevKeycodeResolver : IKeycodeResolver
{
    // Linux evdev input event codes, matching KEY_* in linux/input-event-codes.h. The portal's
    // NotifyKeyboardKeycode takes these codes directly.
    private const int EnterKeycode = 28;
    private const int TabKeycode = 15;
    private const int SpaceKeycode = 57;

    private static readonly Dictionary<char, (int Keycode, bool NeedsShift)> Map = BuildMap();

    /// <summary>
    /// Resolves a character to its US-layout keycode and whether shift is required.
    /// </summary>
    public bool TryResolve(char character, out int keycode, out bool needsShift)
    {
        if (Map.TryGetValue(character, out var entry))
        {
            keycode = entry.Keycode;
            needsShift = entry.NeedsShift;
            return true;
        }

        keycode = 0;
        needsShift = false;
        return false;
    }

    private static Dictionary<char, (int, bool)> BuildMap()
    {
        var map = new Dictionary<char, (int, bool)>
        {
            ['\n'] = (EnterKeycode, false),
            ['\t'] = (TabKeycode, false),
            [' '] = (SpaceKeycode, false),
        };

        // US-layout evdev codes, ordered left-to-right, top-to-bottom across the letter rows.
        AddLetters(map, 'q', 16);
        AddLetters(map, 'w', 17);
        AddLetters(map, 'e', 18);
        AddLetters(map, 'r', 19);
        AddLetters(map, 't', 20);
        AddLetters(map, 'y', 21);
        AddLetters(map, 'u', 22);
        AddLetters(map, 'i', 23);
        AddLetters(map, 'o', 24);
        AddLetters(map, 'p', 25);

        AddLetters(map, 'a', 30);
        AddLetters(map, 's', 31);
        AddLetters(map, 'd', 32);
        AddLetters(map, 'f', 33);
        AddLetters(map, 'g', 34);
        AddLetters(map, 'h', 35);
        AddLetters(map, 'j', 36);
        AddLetters(map, 'k', 37);
        AddLetters(map, 'l', 38);

        AddLetters(map, 'z', 44);
        AddLetters(map, 'x', 45);
        AddLetters(map, 'c', 46);
        AddLetters(map, 'v', 47);
        AddLetters(map, 'b', 48);
        AddLetters(map, 'n', 49);
        AddLetters(map, 'm', 50);

        // Number row: the unshifted digit and the shifted symbol share one physical key.
        AddPair(map, '1', '!', 2);
        AddPair(map, '2', '@', 3);
        AddPair(map, '3', '#', 4);
        AddPair(map, '4', '$', 5);
        AddPair(map, '5', '%', 6);
        AddPair(map, '6', '^', 7);
        AddPair(map, '7', '&', 8);
        AddPair(map, '8', '*', 9);
        AddPair(map, '9', '(', 10);
        AddPair(map, '0', ')', 11);

        AddPair(map, '-', '_', 12);
        AddPair(map, '=', '+', 13);
        AddPair(map, '[', '{', 26);
        AddPair(map, ']', '}', 27);
        AddPair(map, '\\', '|', 43);
        AddPair(map, ';', ':', 39);
        AddPair(map, '\'', '"', 40);
        AddPair(map, '`', '~', 41);
        AddPair(map, ',', '<', 51);
        AddPair(map, '.', '>', 52);
        AddPair(map, '/', '?', 53);

        return map;
    }

    private static void AddLetters(Dictionary<char, (int, bool)> map, char lower, int keycode)
    {
        map[lower] = (keycode, false);
        map[char.ToUpperInvariant(lower)] = (keycode, true);
    }

    private static void AddPair(Dictionary<char, (int, bool)> map, char unshifted, char shifted, int keycode)
    {
        map[unshifted] = (keycode, false);
        map[shifted] = (keycode, true);
    }
}
