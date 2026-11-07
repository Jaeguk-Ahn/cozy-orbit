using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Tests.Update;

public sealed class UpdateInstallerTests : IDisposable
{
    private const string Url = "https://github.com/Jaeguk-Ahn/cozy-orbit/releases/download/release/v1.2.3/CozyOrbit-1.2.3-win-x64.zip";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cozy-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _exe;

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(_dir);
        _exe = Path.Combine(_dir, "CozyOrbit.exe");
        File.WriteAllText(_exe, "old");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class FakeHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            Assert.NotEmpty(request.Headers.UserAgent);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    private static UpdateChecker.Release Release(byte[] zip, string? url = Url, long? size = null, string? sha = null) =>
        new("1.2.3", "release/v1.2.3", "https://github.com/Jaeguk-Ahn/cozy-orbit/releases/tag/release/v1.2.3",
            new UpdateAsset("CozyOrbit-1.2.3-win-x64.zip", size ?? zip.Length, sha ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant()), url);

    private UpdateInstaller Installer(FakeHandler handler, string? flavor = "win-x64") =>
        new(new HttpClient(handler), flavor, "0.1.0", _exe, UpdateInstaller.Kind.WindowsExe, Path.Combine(_dir, "work"));

    [Fact]
    public async Task DownloadsVerifiesAndExtractsTheExe()
    {
        var zip = Zip(("CozyOrbit.exe", "new"));
        var handler = new FakeHandler(zip);
        var staged = await Installer(handler).PrepareAsync(Release(zip), default);
        Assert.Equal("new", File.ReadAllText(staged));
        Assert.Equal([new Uri(Url)], handler.Requests);
        Assert.Equal("old", File.ReadAllText(_exe)); // 설치 자리는 아직 그대로
    }

    [Fact]
    public async Task ChecksumMismatchLeavesNothingBehind()
    {
        var zip = Zip(("CozyOrbit.exe", "new"));
        var installer = Installer(new FakeHandler(zip));
        var ex = await Assert.ThrowsAsync<UpdateInstallException>(() => installer.PrepareAsync(Release(zip, sha: new string('0', 64)), default));
        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "work"), "*.zip*"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task WrongSizeIsRejected(int delta)
    {
        var zip = Zip(("CozyOrbit.exe", "new"));
        var installer = Installer(new FakeHandler(zip));
        await Assert.ThrowsAsync<UpdateInstallException>(() => installer.PrepareAsync(Release(zip, size: zip.Length + delta), default));
    }

    [Fact]
    public async Task ZipWithUnexpectedContentIsRejected()
    {
        foreach (var zip in new[] { Zip(("Other.exe", "x")), Zip(("CozyOrbit.exe", "a"), ("Second.exe", "b")), Zip(("readme.txt", "x")) })
            await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(new FakeHandler(zip)).PrepareAsync(Release(zip), default));
    }

    [Fact]
    public async Task UntrustedDownloadAddressIsRefused()
    {
        var zip = Zip(("CozyOrbit.exe", "new"));
        var handler = new FakeHandler(zip);
        await Assert.ThrowsAsync<UpdateInstallException>(() => Installer(handler).PrepareAsync(Release(zip, url: "https://evil.example/CozyOrbit-1.2.3-win-x64.zip"), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void DevelopmentBuildOnlyNotifies()
    {
        var zip = Zip(("CozyOrbit.exe", "new"));
        var installer = Installer(new FakeHandler(zip), flavor: null);
        Assert.Equal(UpdateInstaller.Blocker.DevBuild, installer.Blocked);
        Assert.NotNull(installer.UnsupportedReason);
        Assert.False(installer.CanInstall(Release(zip)));
    }

    [Fact]
    public void WrongOsFlavorIsBlocked()
    {
        var installer = Installer(new FakeHandler([]), flavor: "osx-arm64");
        Assert.Equal(UpdateInstaller.Blocker.WrongOs, installer.Blocked);
    }

    [Fact]
    public void CanInstallNeedsAssetAndAddress()
    {
        var zip = Zip(("CozyOrbit.exe", "new"));
        var installer = Installer(new FakeHandler(zip));
        Assert.Equal(UpdateInstaller.Blocker.None, installer.Blocked);
        Assert.True(installer.CanInstall(Release(zip)));
        Assert.False(installer.CanInstall(Release(zip, url: null)));
        Assert.False(installer.CanInstall(Release(zip) with { Asset = null }));
        Assert.False(installer.CanInstall(null));
    }

    [Fact]
    public void FinishSwapsTheExeAndRelaunches()
    {
        var source = Path.Combine(_dir, "staged.exe");
        File.WriteAllText(source, "new");
        string? relaunched = null;
        Assert.True(UpdateInstaller.Finish(UpdateInstaller.Kind.WindowsExe, _exe, source, () => true, p => { relaunched = p; return true; }));
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal("old", File.ReadAllText(_exe + ".old"));
        Assert.Equal(_exe, relaunched);
    }

    [Fact]
    public void FinishKeepsTheOldAppWhenItDoesNotExitAndRelaunchesIt()
    {
        var source = Path.Combine(_dir, "staged.exe");
        File.WriteAllText(source, "new");
        string? relaunched = null;
        Assert.False(UpdateInstaller.Finish(UpdateInstaller.Kind.WindowsExe, _exe, source, () => false, p => { relaunched = p; return true; }));
        Assert.Equal("old", File.ReadAllText(_exe));
        // 옛 앱이 이미 끝나는 중이었어도 앱이 사라지지 않게 설치된 것을 다시 띄운다
        Assert.Equal(_exe, relaunched);
    }

    [Fact]
    public void FinishRollsBackWhenTheCopyFails()
    {
        string? relaunched = null;
        // 새 파일이 없으면 복사가 실패한다 → 옛 exe 를 되돌리고 그것을 다시 띄운다
        Assert.False(UpdateInstaller.Finish(UpdateInstaller.Kind.WindowsExe, _exe, Path.Combine(_dir, "missing.exe"), () => true, p => { relaunched = p; return true; }));
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.Equal(_exe, relaunched);
    }

    [Fact]
    public void FinishUpdateArgumentsAreRecognized()
    {
        Assert.Null(UpdateInstaller.RunFinishUpdate(["--after-update"]));
        Assert.Equal(2, UpdateInstaller.RunFinishUpdate([UpdateInstaller.FinishUpdateArg, "exe"]));
    }

    [Theory]
    [InlineData("/Applications/Cozy Orbit.app/Contents/MacOS/CozyOrbit", "/Applications/Cozy Orbit.app")]
    [InlineData("/Applications/CozyOrbit", null)]
    [InlineData("/tmp/x/Contents/MacOS/CozyOrbit", null)]
    public void FindsTheMacBundle(string exe, string? bundle)
    {
        var found = UpdateInstaller.BundleOf(exe);
        Assert.Equal(bundle, found?.Replace('\\', '/'));
    }
}
