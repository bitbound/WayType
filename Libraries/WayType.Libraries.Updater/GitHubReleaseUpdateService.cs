using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Platform;
using WayType.Libraries.Core.Updates;

namespace WayType.Libraries.Updater;

/// <summary>
/// Checks GitHub Releases for a newer build and hands the download off to replace this executable.
/// </summary>
public sealed class GitHubReleaseUpdateService(
    HttpClient httpClient,
    IAppInfo appInfo,
    IPlatformPaths paths,
    ILogger<GitHubReleaseUpdateService> logger) : IUpdateService
{
    private UpdateInfo? _availableUpdate;
    private bool _updateAvailableRaised;

    public UpdateInfo? AvailableUpdate => _availableUpdate;

    public event EventHandler<UpdateInfo>? UpdateAvailable;

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        GitHubRelease? release;

        try
        {
            release = await FetchLatestReleaseAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning(exception, "Could not check for a WayType update.");
            return null;
        }

        if (release is null)
        {
            return null;
        }

        var tag = release.TagName;

        if (!UpdateVersionParser.TryParse(tag, out var releaseVersion))
        {
            logger.LogWarning("Ignoring release with unrecognized version tag \"{Tag}\".", tag);
            return null;
        }

        var currentVersion = UpdateVersionParser.NormalizeCurrent(appInfo.Version);

        if (releaseVersion <= currentVersion)
        {
            logger.LogDebug("WayType {Version} is already the latest release.", currentVersion);
            return null;
        }

        var asset = ReleaseAssetSelector.Select(release);

        if (asset?.Name is not { Length: > 0 } assetName
            || asset.DownloadUrl is not { Length: > 0 } downloadUrl)
        {
            logger.LogWarning(
                "Release {Tag} has no {AssetName} asset, so this update cannot be applied.",
                tag,
                ReleaseAssetSelector.AssetName);
            return null;
        }

        ArgumentNullException.ThrowIfNull(tag);

        var update = new UpdateInfo(tag.Trim(), assetName, downloadUrl);
        _availableUpdate = update;

        if (!_updateAvailableRaised)
        {
            _updateAvailableRaised = true;
            UpdateAvailable?.Invoke(this, update);
        }

        logger.LogInformation("Update available: WayType {Tag}.", update.Version);

        return update;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var update = _availableUpdate ?? await CheckAsync(cancellationToken);

        if (update is null)
        {
            throw new InvalidOperationException("No update is available to apply.");
        }

        Directory.CreateDirectory(paths.UpdateStagingDirectory);

        var downloadPath = Path.Combine(paths.UpdateStagingDirectory, update.AssetName);

        logger.LogInformation("Downloading WayType {Version} to {Path}.", update.Version, downloadPath);

        await using (var target = new FileStream(
                         downloadPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         FileOptions.Asynchronous))
        {
            await using var download = await httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken);
            await download.CopyToAsync(target, cancellationToken);
        }

        // A downloaded release asset is not executable by default; exec-ing it without the bit fails with EACCES.
        UnixExecutablePermissions.MakeExecutable(downloadPath);

        var currentExecutable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(currentExecutable))
        {
            throw new InvalidOperationException("Could not determine the path of the running executable.");
        }

        var handoff = new UpdateHandoff(currentExecutable, Environment.ProcessId);
        var startInfo = new ProcessStartInfo(downloadPath)
        {
            UseShellExecute = false,
        };

        foreach (var argument in UpdateHandoff.BuildArguments(handoff))
        {
            startInfo.ArgumentList.Add(argument);
        }

        logger.LogInformation("Handing off to {Path} to replace {Current}.", downloadPath, currentExecutable);

        Process.Start(startInfo);
    }

    private async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{appInfo.RepositorySlug}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // GitHub rejects requests with no User-Agent; the media type pins the current representation.
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("User-Agent", $"{appInfo.ProductName}/{appInfo.Version}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonSerializer.DeserializeAsync<GitHubRelease>(content, cancellationToken: cancellationToken);
    }
}
