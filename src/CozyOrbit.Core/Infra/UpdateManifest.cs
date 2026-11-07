using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CozyOrbit.Core.Infra;

/// <summary>배포 파일 하나 (매니페스트의 assets 값)</summary>
public sealed record UpdateAsset(string Name, long Size, string Sha256);

/// <summary>
/// 서명된 업데이트 매니페스트 (release.yml → scripts/make-update-manifest.sh 가 만들어 릴리스에 첨부).
/// GitHub 릴리스에서 직접 받지만, 서명이 맞을 때만 그 안의 크기·sha256 을 믿는다 —
/// 저장소나 릴리스 첨부가 바뀌어도 ENV_RELEASE 의 서명 키 없이는 다른 zip 을 깔 수 없다.
/// </summary>
/// <param name="Version">정규화된 버전 (예: 0.2.0)</param>
/// <param name="Tag">git 태그 (예: release/v0.2.0) — GitHub 릴리스의 tag_name 과 같아야 한다</param>
/// <param name="Assets">배포판(RID, 예: win-x64·osx-arm64) → 파일</param>
public sealed record UpdateManifest(string Version, string Tag, IReadOnlyDictionary<string, UpdateAsset> Assets)
{
    public const int SupportedFormat = 1;

    /// <summary>서명 검사용 공개 키 (ECDSA P-256) — scripts/update-signing.pub.pem 과 같아야 한다 (테스트가 확인)</summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFlR1/s81MZVPM2wnXQOHEhxHXhFF
        v+qYTLbg5YpG1oZWoeFnz0v+Wrn50te2v+I4j5qm4jtPI5K7DqdiPwVorw==
        -----END PUBLIC KEY-----
        """;

    /// <summary>zip 하나의 최대 크기 — 이보다 크다고 적혀 있으면 매니페스트를 통째로 버린다</summary>
    internal const long MaxAssetBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>
    /// 서명(받은 글자의 UTF-8 바이트에 대한 DER base64)을 검사하고 읽는다. 서명이 틀리거나, 모르는 형식이거나,
    /// 값이 이상하면 null — 하나라도 의심스러우면 통째로 버린다.
    /// </summary>
    public static UpdateManifest? Verify(string manifestJson, string signatureBase64, string publicKeyPem = PublicKeyPem)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64.Trim()); }
        catch (FormatException) { return null; }
        if (signature.Length is 0 or > 256) return null;

        using (var key = ECDsa.Create())
        {
            key.ImportFromPem(publicKeyPem);
            if (!key.VerifyData(Encoding.UTF8.GetBytes(manifestJson), signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                return null;
        }
        return Parse(manifestJson);
    }

    /// <summary>서명이 맞은 뒤의 값 검사. 형식·버전·태그·파일 이름·크기·sha256 중 하나라도 이상하면 null</summary>
    internal static UpdateManifest? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null;
            if (root["format"]?.GetValue<int>() != SupportedFormat) return null;
            var tag = root["tag"]?.GetValue<string>();
            var version = root["version"]?.GetValue<string>();
            if (tag == null || version == null || UpdateChecker.NormalizedReleaseVersion(version) is not { } v) return null;
            if (tag != UpdateChecker.TagFor(v)) return null;
            if (root["assets"] is not JsonObject assets || assets.Count == 0) return null;

            var map = new Dictionary<string, UpdateAsset>(StringComparer.Ordinal);
            foreach (var (flavor, node) in assets)
            {
                var name = node?["name"]?.GetValue<string>();
                var size = node?["size"]?.GetValue<long>() ?? 0;
                var sha = node?["sha256"]?.GetValue<string>();
                if (!IsSafeName(flavor) || name == null || name != UpdateChecker.AssetName(v, flavor)) return null;
                if (size is <= 0 or > MaxAssetBytes) return null;
                if (sha == null || sha.Length != 64 || !sha.All(Uri.IsHexDigit)) return null;
                map[flavor] = new UpdateAsset(name, size, sha.ToLowerInvariant());
            }
            return new UpdateManifest(v, tag, map);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>파일·키 이름: 영숫자와 . _ - 만 (경로 조각이 끼어들지 않게)</summary>
    internal static bool IsSafeName(string s) =>
        s.Length is > 0 and <= 128 && s != "." && s != ".." && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
