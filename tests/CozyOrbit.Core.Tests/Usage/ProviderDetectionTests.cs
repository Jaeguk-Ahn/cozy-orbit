using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;

namespace CozyOrbit.Core.Tests.Usage;

public sealed class ProviderDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cozy-detect-" + Guid.NewGuid().ToString("N"));
    private readonly string _home;

    public ProviderDetectionTests()
    {
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    private ProviderContext Ctx() => new()
    {
        Now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero),
        Since = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
        Env = _ => null,
        Home = _home,
        RoamingAppData = Path.Combine(_root, "roaming"),
        LocalAppData = Path.Combine(_root, "local"),
    };

    /// <summary>루트를 그대로 돌려주거나(또는 던지는) 가짜 프로바이더</summary>
    private sealed class FakeProvider(string id, Func<IReadOnlyList<string>> roots) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public IReadOnlyList<string> ResolveRoots(ProviderContext ctx) => roots();
        public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) => Task.FromResult<IReadOnlyList<UsageEntry>>(Array.Empty<UsageEntry>());
    }

    [Fact]
    public void FoundOnlyWhenARootFolderExists()
    {
        var gemini = new GeminiUsageProvider();
        Assert.False(ProviderDetection.Found(gemini, Ctx()));

        Directory.CreateDirectory(Path.Combine(_home, ".gemini", "tmp"));
        Assert.True(ProviderDetection.Found(gemini, Ctx()));
    }

    [Fact]
    public void FileRootCountsAndBrokenResolverIsNotFound()
    {
        var file = Path.Combine(_root, "state.db");
        File.WriteAllText(file, "");
        var detected = ProviderDetection.Detect(new IUsageProvider[]
        {
            new FakeProvider("file", () => new[] { Path.Combine(_root, "missing"), file }),
            new FakeProvider("missing", () => new[] { Path.Combine(_root, "nope") }),
            new FakeProvider("broken", () => throw new IOException("boom")),
        }, Ctx());

        Assert.True(detected["file"]);
        Assert.False(detected["missing"]);
        Assert.False(detected["broken"]);
    }

    [Fact]
    public void AnyEnabledFoundIgnoresDisabledAndIsUnknownBeforeDetection()
    {
        var ids = new[] { "a", "b" };
        var none = new HashSet<string>();
        Assert.Null(ProviderDetection.AnyEnabledFound(new Dictionary<string, bool>(), ids, none));

        var found = new Dictionary<string, bool> { ["a"] = true, ["b"] = false };
        Assert.True(ProviderDetection.AnyEnabledFound(found, ids, none));
        // 찾은 도구를 꺼 두면 "켜 둔 것 중엔 없음"
        Assert.False(ProviderDetection.AnyEnabledFound(found, ids, new HashSet<string> { "a" }));
    }
}
