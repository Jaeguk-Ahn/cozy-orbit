using System.Reflection;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Tests.Update;

public class UpdateCheckerTests
{
    private const string Download = "https://github.com/Jaeguk-Ahn/cozy-orbit/releases/download/release/v1.2.3/";

    private static string ReleaseJson(string tag = "release/v1.2.3", bool draft = false, bool prerelease = false,
        string page = "https://github.com/Jaeguk-Ahn/cozy-orbit/releases/tag/release/v1.2.3", string? assets = null) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "{{page}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": {{assets ?? $$"""
            [
              { "name": "CozyOrbit-1.2.3-win-x64.zip", "browser_download_url": "{{Download}}CozyOrbit-1.2.3-win-x64.zip" },
              { "name": "CozyOrbit-1.2.3-osx-arm64.zip", "browser_download_url": "{{Download}}CozyOrbit-1.2.3-osx-arm64.zip" },
              { "name": "CozyOrbit-1.2.3-osx-x64.zip", "browser_download_url": "{{Download}}CozyOrbit-1.2.3-osx-x64.zip" },
              { "name": "update-manifest.json", "browser_download_url": "{{Download}}update-manifest.json" },
              { "name": "update-manifest.json.sig", "browser_download_url": "{{Download}}update-manifest.json.sig" }
            ]
            """}}
        }
        """;

    private static UpdateManifest Manifest(string version = "1.2.3") =>
        UpdateManifest.Parse(UpdateManifestTests.ManifestJson(version))!;

    private static UpdateChecker Checker(string current, string? flavor, UpdateChecker.FetchResult result) =>
        new(current, new UpdateChecker.BuildInfo(flavor), fetch: _ => Task.FromResult(result));

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("release/v1.2.3", "1.2.3")]
    [InlineData("01.002.0003", "1.2.3")]
    [InlineData(" 0.1.0 ", "0.1.0")]
    [InlineData("1.2", null)]
    [InlineData("1.2.3.4", null)]
    [InlineData("1.2.3-beta.1", null)]
    [InlineData("v1.x.3", null)]
    [InlineData("", null)]
    [InlineData("1234567890.0.0", null)]
    public void NormalizesReleaseVersions(string input, string? expected) =>
        Assert.Equal(expected, UpdateChecker.NormalizedReleaseVersion(input));

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.10", "0.1.9", true)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.9", "0.1.10", false)]
    [InlineData("0.0.1", "0.1.0", false)]
    public void ComparesVersionsNumerically(string a, string b, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(a, b));

    [Fact]
    public void CurrentVersionComesFromTheAssembly()
    {
        var v = UpdateChecker.CurrentVersionOf(typeof(UpdateChecker).Assembly);
        Assert.NotNull(UpdateChecker.NormalizedReleaseVersion(v));
        Assert.Equal("0.0.0", UpdateChecker.CurrentVersionOf(null));
    }

    [Fact]
    public void DevelopmentBuildHasNoFlavor()
    {
        // 테스트 어셈블리에는 배포 스크립트가 넣는 CozyUpdateFlavor 가 없다
        Assert.Null(UpdateChecker.BuildInfo.FromAssembly(Assembly.GetExecutingAssembly()).Flavor);
        Assert.False(new UpdateChecker("0.1.0").IsPublishedBuild);
    }

    [Theory]
    [InlineData("win-x64", "CozyOrbit-1.2.3-win-x64.zip")]
    [InlineData("osx-arm64", "CozyOrbit-1.2.3-osx-arm64.zip")]
    [InlineData("osx-x64", "CozyOrbit-1.2.3-osx-x64.zip")]
    public void SelectsTheAssetForEachRid(string rid, string expected)
    {
        Assert.Equal(expected, UpdateChecker.SelectAsset(Manifest(), rid)?.Name);
        Assert.Equal(expected, UpdateChecker.AssetName("1.2.3", rid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("linux-x64")]
    [InlineData("win-arm64")]
    public void UnknownRidHasNoAsset(string? rid) => Assert.Null(UpdateChecker.SelectAsset(Manifest(), rid));

    [Fact]
    public void ParsesPublishedRelease()
    {
        var r = UpdateChecker.ParseRelease(ReleaseJson());
        Assert.NotNull(r);
        Assert.Equal("release/v1.2.3", r!.Tag);
        Assert.Equal(5, r.AssetUrls.Count);
        Assert.Equal(Download + "update-manifest.json.sig", r.AssetUrls[UpdateChecker.SignatureName]);
    }

    [Fact]
    public void IgnoresDraftsPrereleasesAndOddTags()
    {
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson(draft: true)));
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson(prerelease: true)));
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson(tag: "v1.2.3")));
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson(tag: "release/v1.2.3-rc.1")));
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson(page: "https://evil.example/releases/tag/release/v1.2.3")));
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson(page: "http://github.com/Jaeguk-Ahn/cozy-orbit/releases/tag/release/v1.2.3")));
        Assert.Null(UpdateChecker.ParseRelease("{ nope"));
    }

    [Fact]
    public void DropsAssetsOutsideThisRepository()
    {
        var r = UpdateChecker.ParseRelease(ReleaseJson(assets: $$"""
            [
              { "name": "CozyOrbit-1.2.3-win-x64.zip", "browser_download_url": "https://github.com/someone/else/releases/download/x/CozyOrbit-1.2.3-win-x64.zip" },
              { "name": "CozyOrbit-1.2.3-osx-x64.zip", "browser_download_url": "{{Download}}other.zip" },
              { "name": "../evil.zip", "browser_download_url": "{{Download}}../evil.zip" },
              { "name": "CozyOrbit-1.2.3-osx-arm64.zip", "browser_download_url": "http://github.com/Jaeguk-Ahn/cozy-orbit/releases/download/release/v1.2.3/CozyOrbit-1.2.3-osx-arm64.zip" },
              { "name": "update-manifest.json", "browser_download_url": "{{Download}}update-manifest.json" }
            ]
            """));
        Assert.NotNull(r);
        Assert.Equal([UpdateChecker.ManifestName], r!.AssetUrls.Keys);
    }

    [Fact]
    public async Task NewerSignedReleaseIsOfferedWithThisBuildsAsset()
    {
        var release = UpdateChecker.ParseRelease(ReleaseJson())!;
        var c = Checker("0.1.0", "osx-arm64", new(UpdateChecker.Status.Found, release, Manifest()));
        int changes = 0;
        c.Changed += () => changes++;
        await c.CheckAsync();
        Assert.Equal(UpdateChecker.Status.Available, c.LastStatus);
        Assert.Equal("1.2.3", c.Available!.Version);
        Assert.Equal("CozyOrbit-1.2.3-osx-arm64.zip", c.Available.Asset!.Name);
        Assert.Equal(Download + "CozyOrbit-1.2.3-osx-arm64.zip", c.Available.DownloadUrl);
        Assert.Equal(2, changes); // 확인 중 → 결과
    }

    [Fact]
    public async Task DevelopmentBuildIsOfferedWithoutAsset()
    {
        var release = UpdateChecker.ParseRelease(ReleaseJson())!;
        var c = Checker("0.1.0", null, new(UpdateChecker.Status.Found, release, Manifest()));
        await c.CheckAsync();
        Assert.Equal(UpdateChecker.Status.Available, c.LastStatus);
        Assert.Null(c.Available!.Asset);
        Assert.Null(c.Available.DownloadUrl);
    }

    [Fact]
    public async Task MissingAttachmentMeansNoAsset()
    {
        // 매니페스트에는 있어도 릴리스에 붙은 파일이 없으면 받을 곳이 없다
        var release = UpdateChecker.ParseRelease(ReleaseJson(assets: "[]"))!;
        var c = Checker("0.1.0", "win-x64", new(UpdateChecker.Status.Found, release, Manifest()));
        await c.CheckAsync();
        Assert.NotNull(c.Available);
        Assert.Null(c.Available!.Asset);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("2.0.0")]
    public async Task SameOrOlderReleaseIsUpToDate(string current)
    {
        var release = UpdateChecker.ParseRelease(ReleaseJson())!;
        var c = Checker(current, "win-x64", new(UpdateChecker.Status.Found, release, Manifest()));
        await c.CheckAsync();
        Assert.Equal(UpdateChecker.Status.UpToDate, c.LastStatus);
        Assert.Null(c.Available);
    }

    [Theory]
    [InlineData(UpdateChecker.Status.NoRelease)]
    [InlineData(UpdateChecker.Status.RateLimited)]
    [InlineData(UpdateChecker.Status.Untrusted)]
    [InlineData(UpdateChecker.Status.Failed)]
    public async Task FailuresAreReportedQuietly(UpdateChecker.Status status)
    {
        var c = Checker("0.1.0", "win-x64", new(status));
        await c.CheckAsync();
        Assert.Equal(status, c.LastStatus);
        Assert.Null(c.Available);
    }

    [Fact]
    public async Task ExceptionsBecomeFailedAndKeepTheLastOffer()
    {
        var release = UpdateChecker.ParseRelease(ReleaseJson())!;
        bool fail = false;
        var c = new UpdateChecker("0.1.0", new UpdateChecker.BuildInfo("win-x64"), fetch: _ => fail
            ? throw new HttpRequestException("offline")
            : Task.FromResult(new UpdateChecker.FetchResult(UpdateChecker.Status.Found, release, Manifest())));
        await c.CheckAsync();
        fail = true;
        await c.CheckAsync();
        Assert.Equal(UpdateChecker.Status.Failed, c.LastStatus);
        Assert.Equal("1.2.3", c.Available?.Version); // 이미 찾은 새 버전은 그대로 둔다
    }

    [Theory]
    [InlineData("https://github.com/Jaeguk-Ahn/cozy-orbit/releases/download/release/v1.2.3/a.zip", true)]
    [InlineData("https://github.com/jaeguk-ahn/Cozy-Orbit/releases/download/release/v1.2.3/a.zip", true)]
    [InlineData("https://github.com:8443/Jaeguk-Ahn/cozy-orbit/releases/download/a.zip", false)]
    [InlineData("https://user@github.com/Jaeguk-Ahn/cozy-orbit/releases/download/a.zip", false)]
    [InlineData("https://github.com.evil.example/Jaeguk-Ahn/cozy-orbit/releases/download/a.zip", false)]
    [InlineData("https://github.com/Jaeguk-Ahn/cozy-orbit-fake/releases/download/a.zip", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    public void OnlyThisRepositorysDownloadsAreTrusted(string url, bool trusted) =>
        Assert.Equal(trusted, UpdateChecker.IsRepoUrl(url, "/releases/download/"));
}
