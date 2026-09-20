namespace WayType.Libraries.Core.Input;

/// <summary>
/// Resolves an XKB keysym for a character, used by the portal keyboard injection path.
/// </summary>
public interface IKeysymResolver
{
    bool TryResolve(string character, out uint keysym);
}
