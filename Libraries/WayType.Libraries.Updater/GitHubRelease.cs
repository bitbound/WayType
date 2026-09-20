using System.Text.Json.Serialization;

namespace WayType.Libraries.Updater;

/// <summary>
/// The subset of the GitHub "latest release" payload that the updater reads.
/// </summary>
internal sealed record GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset>? Assets { get; init; }
}

/// <summary>
/// A downloadable file attached to a GitHub release.
/// </summary>
internal sealed record GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}
