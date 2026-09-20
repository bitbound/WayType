namespace WayType.Libraries.Updater;

/// <summary>
/// Chooses the release asset the app publishes.
/// </summary>
internal static class ReleaseAssetSelector
{
    /// <summary>
    /// The published single-file binary for this app. Other release assets are ignored.
    /// </summary>
    public const string AssetName = "WayType-linux-x64";

    public static GitHubReleaseAsset? Select(GitHubRelease release)
    {
        var candidates = release.Assets;

        if (candidates is null)
        {
            return null;
        }

        GitHubReleaseAsset? incomplete = null;

        foreach (var asset in candidates)
        {
            if (!string.Equals(asset.Name, AssetName, StringComparison.Ordinal))
            {
                continue;
            }

            // GitHub reports "uploaded" once an asset is fully available and "open" while it is still uploading.
            if (string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }

            incomplete ??= asset;
        }

        return incomplete;
    }
}
