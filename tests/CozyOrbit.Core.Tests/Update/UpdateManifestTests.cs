using System.Security.Cryptography;
using System.Text;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Tests.Update;

public class UpdateManifestTests
{
    private const string Sha = "5bb15f495451c8a1b970e3a8b75735fd4fc039354de10e3eab57b70b61c13089";

    internal static string ManifestJson(string version = "1.2.3", string? tag = null, string? assets = null, int format = 1) =>
        $$"""
        {
          "format": {{format}},
          "version": "{{version}}",
          "tag": "{{tag ?? "release/v" + version}}",
          "assets": {{assets ?? $$"""
            {
              "win-x64": { "name": "CozyOrbit-{{version}}-win-x64.zip", "size": 1000, "sha256": "{{Sha}}" },
              "osx-arm64": { "name": "CozyOrbit-{{version}}-osx-arm64.zip", "size": 2000, "sha256": "{{Sha.ToUpperInvariant()}}" },
              "osx-x64": { "name": "CozyOrbit-{{version}}-osx-x64.zip", "size": 3000, "sha256": "{{Sha}}" }
            }
            """}}
        }
        """;

    internal static (string Pem, Func<string, string> Sign) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportSubjectPublicKeyInfoPem(),
            text => Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));
    }

    [Fact]
    public void GoodSignatureIsAccepted()
    {
        var (pem, sign) = NewKey();
        var json = ManifestJson();
        var m = UpdateManifest.Verify(json, sign(json), pem);
        Assert.NotNull(m);
        Assert.Equal("1.2.3", m!.Version);
        Assert.Equal("release/v1.2.3", m.Tag);
        Assert.Equal(3, m.Assets.Count);
        Assert.Equal(new UpdateAsset("CozyOrbit-1.2.3-osx-arm64.zip", 2000, Sha), m.Assets["osx-arm64"]); // sha256 는 소문자로
    }

    [Fact]
    public void SignatureFromAnotherKeyIsRejected()
    {
        var (pem, _) = NewKey();
        var (_, otherSign) = NewKey();
        var json = ManifestJson();
        Assert.Null(UpdateManifest.Verify(json, otherSign(json), pem));
    }

    [Fact]
    public void TamperedManifestIsRejected()
    {
        var (pem, sign) = NewKey();
        var json = ManifestJson();
        var sig = sign(json);
        Assert.Null(UpdateManifest.Verify(json.Replace("\"size\": 1000", "\"size\": 1001"), sig, pem));
        Assert.Null(UpdateManifest.Verify(json.Replace("1.2.3", "9.9.9"), sig, pem));
        Assert.Null(UpdateManifest.Verify(json + " ", sig, pem)); // 바이트 하나라도 다르면
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void BrokenSignatureIsRejected(string sig)
    {
        var (pem, _) = NewKey();
        Assert.Null(UpdateManifest.Verify(ManifestJson(), sig, pem));
    }

    [Fact]
    public void RealFixtureVerifiesWithEmbeddedKey()
    {
        // scripts/make-update-manifest.sh(openssl) 가 실제 서명 키로 만든 것 — openssl 의 DER 서명을 .NET 이 읽는지까지 본다.
        // 버전 0.0.1 이라 어떤 앱에도 "새 버전" 이 아니다. git 이 줄 끝을 CRLF 로 바꿨을 수 있어 LF 로 되돌린다 (스크립트는 LF 로 쓴다)
        var dir = Path.Combine(RepoRoot.Path, "tests", "CozyOrbit.Core.Tests", "Fixtures", "Update");
        var json = File.ReadAllText(Path.Combine(dir, "update-manifest.json")).ReplaceLineEndings("\n");
        var sig = File.ReadAllText(Path.Combine(dir, "update-manifest.json.sig"));
        var m = UpdateManifest.Verify(json, sig);
        Assert.NotNull(m);
        Assert.Equal("0.0.1", m!.Version);
        Assert.Equal(["osx-arm64", "osx-x64", "win-x64"], m.Assets.Keys.Order());
        Assert.Null(UpdateManifest.Verify(json.Replace("\"size\": 13", "\"size\": 14"), sig));
    }

    [Fact]
    public void PublicKeyFileMatchesEmbeddedKey()
    {
        var file = File.ReadAllText(Path.Combine(RepoRoot.Path, "scripts", "update-signing.pub.pem"));
        Assert.Equal(Normalize(UpdateManifest.PublicKeyPem), Normalize(file));

        // 실제로 P-256 공개 키여야 한다
        using var key = ECDsa.Create();
        key.ImportFromPem(UpdateManifest.PublicKeyPem);
        Assert.Equal(256, key.KeySize);

        static string Normalize(string pem) => string.Join("\n", pem.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    public static TheoryData<string> SuspiciousManifests => new()
    {
        ManifestJson(format: 2),
        ManifestJson(tag: "release/v1.2.4"),          // 태그와 버전이 다름
        ManifestJson(tag: "v1.2.3"),                  // 저장소 태그 규칙이 아님
        ManifestJson(version: "1.2.3-beta.1"),        // 프리릴리스
        ManifestJson(assets: "{}"),                   // 파일 없음
        ManifestJson(assets: "[]"),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.exe", "size": 1, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-osx-x64.zip", "size": 1, "sha256": "{{Sha}}" } }"""), // 키와 이름이 다름
        ManifestJson(assets: $$"""{ "win-x64": { "name": "../CozyOrbit-1.2.3-win-x64.zip", "size": 1, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: $$"""{ "../x": { "name": "CozyOrbit-1.2.3-../x.zip", "size": 1, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": 0, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": -5, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": 99999999999, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": 1.5, "sha256": "{{Sha}}" } }"""),
        ManifestJson(assets: """{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": 1, "sha256": "abc" } }"""),
        ManifestJson(assets: $$"""{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": 1, "sha256": "{{Sha[..63]}}z" } }"""),
        ManifestJson(assets: """{ "win-x64": { "name": "CozyOrbit-1.2.3-win-x64.zip", "size": 1 } }"""),
        "[]",
        "{ not json",
    };

    [Theory]
    [MemberData(nameof(SuspiciousManifests))]
    public void SignedButSuspiciousManifestIsRejected(string json)
    {
        // 서명이 맞아도 값이 이상하면 통째로 버린다
        var (pem, sign) = NewKey();
        Assert.Null(UpdateManifest.Verify(json, sign(json), pem));
    }
}

/// <summary>저장소 뿌리 (CozyOrbit.slnx 가 있는 폴더) — 출력 폴더로 복사되지 않는 파일을 읽을 때</summary>
internal static class RepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "CozyOrbit.slnx"))) return dir.FullName;
        throw new InvalidOperationException("CozyOrbit.slnx 를 찾지 못했어요");
    }
}
