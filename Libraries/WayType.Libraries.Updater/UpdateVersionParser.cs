namespace WayType.Libraries.Updater;

/// <summary>
/// Parses release tags into comparable versions.
/// </summary>
internal static class UpdateVersionParser
{
    /// <summary>
    /// Parses a release tag such as "v1.2.3", "1.2.3" or "1.2.3+build" into a version. Returns false for junk.
    /// </summary>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0);

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim();

        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V'))
        {
            text = text[1..];
        }

        // Semver build and prerelease suffixes (for example "+build.5" or "-rc1") are not part of Version's
        // numeric core, so compare only the leading dotted numbers.
        var suffixIndex = text.IndexOfAny(['+', '-']);

        if (suffixIndex >= 0)
        {
            text = text[..suffixIndex];
        }

        if (!Version.TryParse(text, out var parsed))
        {
            return false;
        }

        // A parsed "1.2" leaves Build and Revision at -1, which would sort below a current "1.2.0".
        // Normalizing negatives to zero keeps the comparison based on the numbers the tag actually carries.
        version = new Version(parsed.Major, parsed.Minor, ZeroIfNegative(parsed.Build), ZeroIfNegative(parsed.Revision));

        return true;
    }

    public static Version NormalizeCurrent(Version current)
    {
        return new Version(current.Major, current.Minor, ZeroIfNegative(current.Build), ZeroIfNegative(current.Revision));
    }

    private static int ZeroIfNegative(int value)
    {
        return value < 0 ? 0 : value;
    }
}
