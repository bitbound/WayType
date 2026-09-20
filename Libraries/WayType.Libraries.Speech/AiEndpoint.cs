using WayType.Libraries.Core.Speech;

namespace WayType.Libraries.Speech;

/// <summary>
/// Composes request URLs from the endpoint the user typed.
/// </summary>
internal static class AiEndpoint
{
    private const string VersionSegment = "/v1";

    /// <summary>
    /// Joins a user-entered base with a path so the version segment appears exactly once.
    /// The base may already carry it, and it may or may not have a trailing slash.
    /// </summary>
    public static Uri BuildUri(string? endpoint, string path)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new AiEndpointException("No AI endpoint is configured.");
        }

        var normalized = endpoint.Trim().TrimEnd('/');

        if (!normalized.EndsWith(VersionSegment, StringComparison.OrdinalIgnoreCase))
        {
            normalized += VersionSegment;
        }

        return new Uri(normalized + path);
    }
}
