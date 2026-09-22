using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Core.Updates;
using WayType.Libraries.Updater;

namespace WayType.Tests;

public class GitHubReleaseUpdateServiceTests
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/TestOrg/WayType/releases/latest";

    private static string ExpectedAssetName => ReleaseAssetSelector.AssetName;

    [Fact]
    public async Task CheckAsync_WhenReleaseIsNewer_ReturnsTheAssetAndRaisesTheEventOnce()
    {
        var (service, handler) = Create(Release("v1.5.0", withAsset: true));
        var raised = 0;
        service.UpdateAvailable += (_, _) => raised++;
        var ct = TestContext.Current.CancellationToken;

        var update = await service.CheckAsync(ct);

        Assert.Equal($"https://downloads.example.test/{ExpectedAssetName}", update?.DownloadUrl);
        Assert.Equal(ExpectedAssetName, update?.AssetName);
        Assert.Equal("v1.5.0", update?.Version);
        Assert.Equal(update, service.AvailableUpdate);

        await service.CheckAsync(ct);

        Assert.Equal(1, raised);
        Assert.Equal(LatestReleaseUrl, handler.RequestUris[0]);
    }

    [Theory]
    [InlineData("v0.9.9")]
    [InlineData("v1.0.0")]
    public async Task CheckAsync_WhenReleaseIsNotNewer_ReturnsNull(string tag)
    {
        var (service, _) = Create(Release(tag, withAsset: true));

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(update);
        Assert.Null(service.AvailableUpdate);
    }

    [Fact]
    public async Task CheckAsync_WhenTagIsNotAVersion_ReturnsNull()
    {
        var (service, _) = Create(Release("nightly-build", withAsset: true));

        Assert.Null(await service.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_WhenReleaseHasNoUsableAsset_ReturnsNull()
    {
        var (service, _) = Create(Release("v2.0.0", withAsset: false));

        Assert.Null(await service.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_WhenGitHubReturnsAnError_ReturnsNull()
    {
        var (service, _) = Create(_ => StubHttpMessageHandler.Json("""{"message":"server error"}""", HttpStatusCode.InternalServerError));

        Assert.Null(await service.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_WhenGitHubIsUnreachable_ReturnsNull()
    {
        var (service, _) = Create(_ => throw new HttpRequestException("no route to host"));

        Assert.Null(await service.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_WhenReleaseOnlyHasTheUnsuffixedAsset_StillReturnsAnUpdate()
    {
        var (service, _) = Create(ReleaseWithAssets("v1.5.0", "waytype"));

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("waytype", update?.AssetName);
        Assert.Equal("https://downloads.example.test/waytype", update?.DownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_WhenBothNamesExist_PrefersTheArchitectureSpecificAsset()
    {
        var (service, _) = Create(ReleaseWithAssets("v1.5.0", "waytype", ExpectedAssetName));

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedAssetName, update?.AssetName);
    }

    [Fact]
    public async Task CheckAsync_WhenNameDiffersOnlyByCase_ReturnsAnUpdate()
    {
        // Releases used to publish the asset with a capital letter, and the old lookup compared with
        // StringComparison.Ordinal, so it never matched.
        var legacySpelling = char.ToUpperInvariant(ExpectedAssetName[0]) + ExpectedAssetName[1..];
        var (service, _) = Create(ReleaseWithAssets("v1.5.0", legacySpelling));

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(legacySpelling, update?.AssetName);
    }

    private static (GitHubReleaseUpdateService Service, StubHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Version? currentVersion = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var service = new GitHubReleaseUpdateService(
            new HttpClient(handler),
            new FakeAppInfo(currentVersion),
            new TestPlatformPaths(),
            NullLogger<GitHubReleaseUpdateService>.Instance);

        return (service, handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Release(string tag, bool withAsset) =>
        ReleaseWithAssets(tag, withAsset ? [ExpectedAssetName] : ["WayType-macos-arm64"]);

    private static Func<HttpRequestMessage, HttpResponseMessage> ReleaseWithAssets(
        string tag,
        params string[] assetNames)
    {
        var assets = string.Join(
            ",",
            assetNames.Select(name =>
                $$"""{"name":"{{name}}","browser_download_url":"https://downloads.example.test/{{name}}","state":"uploaded"}"""));

        var payload = $$"""{"tag_name":"{{tag}}","assets":[{{assets}}]}""";

        return _ => StubHttpMessageHandler.Json(payload);
    }
}

public class UpdateHandoffTests
{
    [Fact]
    public void BuildArguments_RoundTripsThroughTryParse()
    {
        var handoff = new UpdateHandoff("/home/user/.local/bin/waytype", 4242);

        var parsed = UpdateHandoff.TryParse(UpdateHandoff.BuildArguments(handoff));

        Assert.Equal(handoff, parsed);
    }

    [Fact]
    public void TryParse_WithoutHandoffArguments_ReturnsNull()
    {
        Assert.Null(UpdateHandoff.TryParse(["--flag", "value"]));
        Assert.Null(UpdateHandoff.TryParse([]));
    }

    [Fact]
    public void TryParse_WithUnreadableProcessId_ReturnsNull()
    {
        var args = new[]
        {
            UpdateHandoff.TargetArgument,
            "/tmp/waytype",
            UpdateHandoff.ProcessIdArgument,
            "not-a-pid",
        };

        Assert.Null(UpdateHandoff.TryParse(args));
    }
}

public class ReleaseAssetSelectorTests
{
    [Theory]
    [InlineData(Architecture.X64, "waytype-x64")]
    [InlineData(Architecture.Arm64, "waytype-arm64")]
    public void BuildAssetName_ForASupportedArchitecture_AppendsThePlatformSuffix(
        Architecture architecture,
        string expected)
    {
        Assert.Equal(expected, ReleaseAssetSelector.BuildAssetName(architecture));
    }

    [Fact]
    public void BuildCandidateNames_PrefersTheSuffixedAssetAndKeepsTheBareOneAsFallback()
    {
        Assert.Equal(
            new[] { "waytype-arm64", "waytype" },
            ReleaseAssetSelector.BuildCandidateNames(Architecture.Arm64));
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    public void BuildCandidateNames_ForAnUnsupportedArchitecture_IsTheBareName(Architecture architecture)
    {
        Assert.Equal(new[] { "waytype" }, ReleaseAssetSelector.BuildCandidateNames(architecture));
        Assert.Equal("waytype", ReleaseAssetSelector.BuildAssetName(architecture));
    }
}

public class ReleaseWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_PublishesTheAssetNameTheUpdaterLooksFor()
    {
        var workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release.yml"));

        // The name lives in two places that cannot see each other, so pin them together here.
        Assert.Contains($"ASSET_NAME: {ReleaseAssetSelector.AssetName}", workflow, StringComparison.Ordinal);
        Assert.Contains("./artifacts/$ASSET_NAME", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WayType.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root above the test binaries.");
    }
}
