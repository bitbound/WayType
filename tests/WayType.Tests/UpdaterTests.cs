using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WayType.Libraries.Core.Updates;
using WayType.Libraries.Updater;

namespace WayType.Tests;

public class GitHubReleaseUpdateServiceTests
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/TestOrg/WayType/releases/latest";

    [Fact]
    public async Task CheckAsync_WhenReleaseIsNewer_ReturnsTheAssetAndRaisesTheEventOnce()
    {
        var (service, handler) = Create(Release("v1.5.0", withAsset: true));
        var raised = 0;
        service.UpdateAvailable += (_, _) => raised++;
        var ct = TestContext.Current.CancellationToken;

        var update = await service.CheckAsync(ct);

        Assert.Equal("https://downloads.example.test/WayType-linux-x64", update?.DownloadUrl);
        Assert.Equal("WayType-linux-x64", update?.AssetName);
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

    private static Func<HttpRequestMessage, HttpResponseMessage> Release(string tag, bool withAsset)
    {
        var asset = withAsset
            ? """{"name":"WayType-linux-x64","browser_download_url":"https://downloads.example.test/WayType-linux-x64","state":"uploaded"}"""
            : """{"name":"WayType-macos-arm64","browser_download_url":"https://downloads.example.test/other","state":"uploaded"}""";

        var payload = $$"""{"tag_name":"{{tag}}","assets":[{{asset}}]}""";

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
