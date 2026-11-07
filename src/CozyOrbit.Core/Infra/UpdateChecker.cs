using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace CozyOrbit.Core.Infra;

/// <summary>
/// 새 버전 확인. 공개 저장소의 GitHub 릴리스(API 의 releases/latest — 초안·프리릴리스는 빠진다)를 직접 묻고,
/// 첨부된 update-manifest.json 의 서명(UpdateManifest)이 맞을 때만 믿는다. 서명 안 된 정보로는 "새 버전" 도 말하지 않는다.
/// 설치는 UpdateInstaller — 이 클래스는 무엇이 있는지만 안다. 시계·난수는 쓰지 않고, 언제 물을지는 부르는 쪽(UpdateService)이 정한다.
/// </summary>
public sealed class UpdateChecker
{
    public const string Repository = "Jaeguk-Ahn/cozy-orbit";
    public const string LatestApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/" + Repository + "/releases";
    public const string ManifestName = "update-manifest.json";
    public const string SignatureName = ManifestName + ".sig";

    /// <summary>배포판(RID) 이름 — 배포 스크립트가 만드는 zip 과 같다</summary>
    public static readonly IReadOnlyList<string> KnownFlavors = ["win-x64", "osx-arm64", "osx-x64"];

    private const int MaxApiBytes = 1024 * 1024;
    private const int MaxManifestBytes = 64 * 1024;
    private const int MaxSignatureBytes = 1024;

    /// <param name="Version">정규화된 버전</param>
    /// <param name="Tag">git 태그 (release/vX.Y.Z)</param>
    /// <param name="PageUrl">릴리스 페이지 — 자동 설치를 못 할 때 연다</param>
    /// <param name="Asset">이 배포판의 파일 (매니페스트에 없거나 개발 빌드면 null → 설치 대신 페이지)</param>
    /// <param name="DownloadUrl">그 파일의 내려받기 주소 (이 저장소의 releases/download 만)</param>
    public sealed record Release(string Version, string Tag, string PageUrl, UpdateAsset? Asset, string? DownloadUrl);

    /// <summary>GitHub API 응답에서 꺼낸 것 (서명 검사 전)</summary>
    /// <param name="AssetUrls">첨부 파일 이름 → browser_download_url</param>
    public sealed record GitHubRelease(string Tag, string PageUrl, IReadOnlyDictionary<string, string> AssetUrls);

    /// <summary>한 번 물어본 결과. Release 는 Status 가 Found 일 때만</summary>
    public sealed record FetchResult(Status Status, GitHubRelease? Release = null, UpdateManifest? Manifest = null);

    public enum Status
    {
        /// <summary>아직 묻지 않음</summary>
        NotChecked,
        Checking,
        /// <summary>서명이 맞는 최신 릴리스를 찾음 (내부용 — 화면에는 UpToDate·Available 로)</summary>
        Found,
        UpToDate,
        Available,
        /// <summary>공개된 릴리스가 없음 (404)</summary>
        NoRelease,
        /// <summary>GitHub API 요청 한도 (403·429) — 다음 기회에</summary>
        RateLimited,
        /// <summary>서명이 없거나 틀림, 값이 이상함</summary>
        Untrusted,
        /// <summary>네트워크·서버 오류</summary>
        Failed,
    }

    /// <summary>
    /// 빌드에 들어간 업데이트 설정: 이 실행 파일의 배포판(매니페스트 키, 예: win-x64·osx-arm64).
    /// 배포 스크립트가 -p:CozyUpdateFlavor=… 로 넣고(AssemblyMetadata), 개발 빌드엔 없다 → 알리기만 하고 설치하지 않는다.
    /// </summary>
    public sealed record BuildInfo(string? Flavor)
    {
        public const string MetadataKey = "CozyUpdateFlavor";

        public static BuildInfo FromAssembly(Assembly? assembly)
        {
            var value = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == MetadataKey)?.Value;
            return new BuildInfo(value != null && KnownFlavors.Contains(value) ? value : null);
        }
    }

    private readonly Func<CancellationToken, Task<FetchResult>> _fetch;
    private int _inFlight;

    public string CurrentVersion { get; }
    public string? Flavor { get; }
    /// <summary>배포 빌드인가 (배포판이 들어 있음) — 아니면 새 버전을 알리기만 한다</summary>
    public bool IsPublishedBuild => Flavor != null;
    public Status LastStatus { get; private set; } = Status.NotChecked;
    /// <summary>지금 버전보다 높은, 서명이 맞는 릴리스 (마지막으로 성공한 조회 기준 — 그 뒤 실패해도 지우지 않는다)</summary>
    public Release? Available { get; private set; }

    public event Action? Changed;

    public UpdateChecker(string currentVersion, BuildInfo? build = null, HttpClient? http = null,
        Func<CancellationToken, Task<FetchResult>>? fetch = null, string publicKeyPem = UpdateManifest.PublicKeyPem)
    {
        CurrentVersion = NormalizedReleaseVersion(currentVersion) ?? currentVersion;
        Flavor = build?.Flavor;
        _fetch = fetch ?? (ct => LiveFetchAsync(http ?? new HttpClient(), CurrentVersion, publicKeyPem, ct));
    }

    /// <summary>
    /// 최신 릴리스 조회. 이미 묻는 중이면 아무것도 하지 않는다. 실패는 LastStatus 에만 남기고 예외를 내지 않는다.
    /// </summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _inFlight, 1) == 1) return;
        LastStatus = Status.Checking;
        Changed?.Invoke();
        try
        {
            var result = await _fetch(ct);
            LastStatus = result.Status;
            if (result is { Status: Status.Found, Release: { } r, Manifest: { } m }) LastStatus = Consider(r, m);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { LastStatus = Status.Failed; }
        catch (Exception ex)
        {
            AppLog.Warn($"업데이트 확인 실패 ({ex.GetType().Name}): {ex.Message}");
            LastStatus = Status.Failed;
        }
        finally { Interlocked.Exchange(ref _inFlight, 0); }
        Changed?.Invoke();
    }

    /// <summary>서명이 맞은 릴리스를 지금 버전과 견준다</summary>
    internal Status Consider(GitHubRelease release, UpdateManifest manifest)
    {
        if (!IsNewer(manifest.Version, CurrentVersion))
        {
            Available = null;
            return Status.UpToDate;
        }
        var asset = SelectAsset(manifest, Flavor);
        string? url = asset != null && release.AssetUrls.TryGetValue(asset.Name, out var u) ? u : null;
        Available = new Release(manifest.Version, manifest.Tag, release.PageUrl, url != null ? asset : null, url);
        return Status.Available;
    }

    /// <summary>이 배포판의 파일 (배포판을 모르거나 매니페스트에 없으면 null)</summary>
    public static UpdateAsset? SelectAsset(UpdateManifest manifest, string? flavor) =>
        flavor != null && manifest.Assets.TryGetValue(flavor, out var a) ? a : null;

    /// <summary>배포 zip 이름 — scripts/publish*.{ps1,sh} 와 같다</summary>
    public static string AssetName(string version, string flavor) => $"CozyOrbit-{version}-{flavor}.zip";

    /// <summary>git 태그 이름 — 저장소의 태그 규칙 release/vX.Y.Z</summary>
    public static string TagFor(string version) => "release/v" + version;

    /// <summary>실행 중인 앱의 버전 (csproj 의 Version — InformationalVersion 의 +커밋 부분은 뗀다)</summary>
    public static string CurrentVersionOf(Assembly? assembly)
    {
        var info = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info != null && NormalizedReleaseVersion(info.Split('+')[0]) is { } v) return v;
        var n = assembly?.GetName().Version;
        return n == null ? "0.0.0" : $"{n.Major}.{n.Minor}.{Math.Max(0, n.Build)}";
    }

    /// <summary>
    /// 태그·버전 → 비교 가능한 버전. "MAJOR.MINOR.PATCH" (앞의 release/·v 허용)만 — 프리릴리스(v1.0.0-beta.1)나 이상한 값은 null.
    /// (숫자가 아닌 부분을 0 으로 읽으면 베타가 정식과 같게, 쓰레기가 0.0.0 으로 비교된다)
    /// </summary>
    public static string? NormalizedReleaseVersion(string tag)
    {
        var v = tag.Trim();
        if (v.StartsWith("release/", StringComparison.Ordinal)) v = v["release/".Length..];
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        var parts = v.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length is 0 or > 9 || !p.All(char.IsAsciiDigit))) return null;
        return string.Join(".", parts.Select(p => int.Parse(p).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>a 가 b 보다 높은가 (구간별 숫자 비교: 2.0.10 > 2.0.9)</summary>
    public static bool IsNewer(string a, string b)
    {
        var pa = a.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var pb = b.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>
    /// GitHub API 의 릴리스 하나(JSON)를 읽는다. 초안·프리릴리스, 이상한 태그, 이 저장소가 아닌 주소는 null.
    /// 첨부 파일은 이름이 안전하고 주소가 이 저장소의 releases/download/…/이름 인 것만 남긴다.
    /// </summary>
    public static GitHubRelease? ParseRelease(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null;
            if (root["draft"]?.GetValue<bool>() == true || root["prerelease"]?.GetValue<bool>() == true) return null;
            var tag = root["tag_name"]?.GetValue<string>();
            var page = root["html_url"]?.GetValue<string>();
            if (tag == null || NormalizedReleaseVersion(tag) is not { } v || tag != TagFor(v)) return null;
            if (page == null || !IsRepoUrl(page, "/releases/")) return null;

            var urls = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root["assets"] is JsonArray assets)
                foreach (var a in assets)
                {
                    var name = a?["name"]?.GetValue<string>();
                    var url = a?["browser_download_url"]?.GetValue<string>();
                    if (name == null || url == null || !UpdateManifest.IsSafeName(name)) continue;
                    if (!IsRepoUrl(url, "/releases/download/") || !new Uri(url).AbsolutePath.EndsWith("/" + name, StringComparison.Ordinal)) continue;
                    urls[name] = url;
                }
            return new GitHubRelease(tag, page, urls);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>https://github.com/Jaeguk-Ahn/cozy-orbit{pathPrefix}… 인가 (다른 사이트·스킴·포트는 열지도 받지도 않는다)</summary>
    public static bool IsRepoUrl(string url, string pathPrefix = "/") =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && u.UserInfo.Length == 0
        && string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.StartsWith("/" + Repository + pathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 실제 조회: releases/latest → 첨부된 매니페스트·서명 받기 → 서명 검사 → 매니페스트 태그가 릴리스 태그와 같은지.
    /// 릴리스 없음(404)·요청 한도(403/429)·서명 문제는 조용히 상태로만 돌려준다.
    /// </summary>
    internal static async Task<FetchResult> LiveFetchAsync(HttpClient http, string currentVersion, string publicKeyPem, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var req = new HttpRequestMessage(HttpMethod.Get, LatestApiUrl);
        AddHeaders(req, currentVersion);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (resp.StatusCode == HttpStatusCode.NotFound) return new FetchResult(Status.NoRelease);
        if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) return new FetchResult(Status.RateLimited);
        if (!resp.IsSuccessStatusCode) return new FetchResult(Status.Failed);
        var json = await ReadLimitedAsync(resp, MaxApiBytes, timeout.Token);
        if (json == null || ParseRelease(json) is not { } release) return new FetchResult(Status.Untrusted);

        if (!release.AssetUrls.TryGetValue(ManifestName, out var manifestUrl) || !release.AssetUrls.TryGetValue(SignatureName, out var sigUrl))
        {
            AppLog.Warn($"업데이트: {release.Tag} 에 서명된 매니페스트가 없어 무시해요");
            return new FetchResult(Status.Untrusted);
        }
        var manifestText = await DownloadTextAsync(http, manifestUrl, MaxManifestBytes, currentVersion, timeout.Token);
        var signature = await DownloadTextAsync(http, sigUrl, MaxSignatureBytes, currentVersion, timeout.Token);
        if (manifestText == null || signature == null) return new FetchResult(Status.Failed);

        if (UpdateManifest.Verify(manifestText, signature, publicKeyPem) is not { } manifest)
        {
            AppLog.Warn($"업데이트 매니페스트 서명이 맞지 않아 무시해요 ({release.Tag})");
            return new FetchResult(Status.Untrusted);
        }
        if (manifest.Tag != release.Tag) return new FetchResult(Status.Untrusted);
        return new FetchResult(Status.Found, release, manifest);
    }

    internal static void AddHeaders(HttpRequestMessage req, string currentVersion)
    {
        // GitHub API 는 User-Agent 없는 요청을 거절한다
        req.Headers.UserAgent.ParseAdd($"CozyOrbit/{currentVersion}");
    }

    private static async Task<string?> DownloadTextAsync(HttpClient http, string url, int maxBytes, string currentVersion, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req, currentVersion);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return resp.IsSuccessStatusCode ? await ReadLimitedAsync(resp, maxBytes, ct) : null;
    }

    /// <summary>본문을 최대 maxBytes 까지 UTF-8 로 — 더 크면 null (엉뚱한 큰 응답을 끝까지 읽지 않게)</summary>
    private static async Task<string?> ReadLimitedAsync(HttpResponseMessage resp, int maxBytes, CancellationToken ct)
    {
        if (resp.Content.Headers.ContentLength is long length && length > maxBytes) return null;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + n > maxBytes) return null;
            buffer.Write(chunk, 0, n);
        }
        return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
