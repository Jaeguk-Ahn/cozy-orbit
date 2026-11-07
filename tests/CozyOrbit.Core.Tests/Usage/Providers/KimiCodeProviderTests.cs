using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>
/// Kimi Code wire.jsonl 파싱 (원본 KimiCodeUsageTests 재사용).
/// 레이아웃: &lt;home&gt;/.kimi-code/sessions/&lt;workDirKey&gt;/&lt;sessionId&gt;/agents/&lt;agent&gt;/wire.jsonl
/// </summary>
public sealed class KimiCodeProviderTests : IDisposable
{
    private const long T = 1_779_256_800_000; // 2026-05-20T06:00:00Z
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FileTimeUtc = Now.AddHours(-1).UtcDateTime;

    private readonly string _home;
    private readonly string _sessions;

    public KimiCodeProviderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "cozy-kimi-" + Guid.NewGuid().ToString("N"));
        _sessions = Path.Combine(_home, ".kimi-code", "sessions");
        Directory.CreateDirectory(_sessions);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); }
        catch (Exception) { /* 무시 */ }
    }

    private string WriteWire(string text, string session = "ses_1", string agent = "main", string? root = null)
    {
        var dir = Path.Combine(root ?? _sessions, "wd_proj_abc", session, "agents", agent);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "wire.jsonl");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, FileTimeUtc);
        return path;
    }

    private static string Record(long time, string model = "k3-agent", string? scope = "turn",
        long input = 100, long output = 20, long read = 500, long write = 30)
    {
        var scopeField = scope != null ? ",\"usageScope\":\"" + scope + "\"" : "";
        return FormattableString.Invariant(
            $"{{\"type\":\"usage.record\",\"model\":\"{model}\",\"usage\":{{\"inputOther\":{input},\"output\":{output},\"inputCacheRead\":{read},\"inputCacheCreation\":{write}}}{scopeField},\"time\":{time}}}");
    }

    private ProviderContext Ctx(Func<string, string?>? env = null) => new()
    {
        Now = Now,
        Since = Since,
        Env = env ?? (_ => null),
        Home = _home,
        RoamingAppData = Path.Combine(_home, "Roaming"),
        LocalAppData = Path.Combine(_home, "Local"),
    };

    private Task<IReadOnlyList<UsageEntry>> Load(Func<string, string?>? env = null) => new KimiCodeUsageProvider().LoadAsync(Ctx(env));

    [Fact]
    public void Identity()
    {
        IUsageProvider p = new KimiCodeUsageProvider();
        Assert.Equal("kimi_code", p.Id);
        Assert.Equal("Kimi Code", p.DisplayName);
        Assert.True(p.IncludeModels);
    }

    [Fact]
    public async Task ParsesUsageRecordMapping()
    {
        WriteWire(
            "{\"type\":\"turn.begin\",\"turnId\":\"t1\",\"time\":1779256800292}\n" +
            "{\"type\":\"usage.record\",\"model\":\"kimi-k2\",\"usage\":{\"inputOther\":10,\"output\":5,\"inputCacheRead\":7,\"inputCacheCreation\":3},\"usageScope\":\"turn\",\"time\":1779256800302}");
        var e = Assert.Single(await Load());
        Assert.Equal("kimi-k2", e.Model);
        Assert.Equal(10, e.Input);
        Assert.Equal(5, e.Output);
        Assert.Equal(7, e.CacheRead);
        Assert.Equal(3, e.CacheWrite);
        Assert.Equal(25, e.Total);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_779_256_800_302), e.Date);
        Assert.Equal(UsageTime.LocalDay(e.Date), e.LocalDay);
        Assert.Equal("kimi|1779256800302|kimi-k2|turn|10|5|7|3", e.Id);
        Assert.Null(e.ExplicitCost);
    }

    [Fact]
    public async Task CountsTurnSessionAndMissingScopeSkipsUnknown()
    {
        WriteWire(string.Join("\n",
            Record(T, scope: "turn"),
            Record(T + 1, scope: "session"),
            Record(T + 2, scope: null),
            Record(T + 3, scope: "cumulative")));
        Assert.Equal(3, (await Load()).Count);
    }

    [Fact]
    public async Task SkipsMalformedLinesAndDefaultsModel()
    {
        WriteWire(string.Join("\n",
            "{\"type\":\"message\",\"text\":\"what is \\\"usage.record\\\"?\",\"time\":" + T + "}",
            "{\"type\":\"usage.record\",\"usage\":{\"inputOther\":1,\"output\":1},\"usageScope\":\"turn\"}",
            "{\"type\":\"usage.record\",\"usage\":{\"inputOther\":1,\"output\":1},\"usageScope\":\"turn\",\"time\":0}",
            "{\"type\":\"usage.record\",\"model\":\"k3-agent\",\"usageScope\":\"turn\",\"time\":" + T + "}",
            "{\"type\":\"usage.record\",\"model\":\"k3-agent\",\"usage\":{\"inputOther\":1,",
            "{\"type\":\"usage.record\",\"model\":\"\",\"usage\":{\"inputOther\":4,\"output\":2},\"usageScope\":\"turn\",\"time\":" + T + "}"));
        var e = Assert.Single(await Load());
        Assert.Equal("kimi-code", e.Model);
        Assert.Equal(6, e.Total);
    }

    [Fact]
    public async Task PartialUsageDefaultsMissingBuckets()
    {
        WriteWire("{\"type\":\"usage.record\",\"model\":\"k3-agent\",\"usage\":{\"inputCacheRead\":9},\"usageScope\":\"turn\",\"time\":1779256800000}");
        var e = Assert.Single(await Load());
        Assert.Equal(0, e.Input);
        Assert.Equal(0, e.Output);
        Assert.Equal(9, e.CacheRead);
        Assert.Equal(0, e.CacheWrite);
    }

    [Fact]
    public async Task ForkCopyDedupsAndSubagentsCount()
    {
        var original = new[] { Record(T), Record(T + 1000, output: 40) };
        WriteWire(string.Join("\n", original), session: "ses_orig");
        WriteWire(string.Join("\n", original.Append(Record(T + 5000, output: 7))), session: "ses_fork");
        WriteWire(Record(T + 2000, model: "k2d6-agent"), session: "ses_orig", agent: "sub_1");
        var entries = await Load();
        Assert.Equal(4, entries.Count); // 원본 2 + fork 전용 1 + 서브에이전트 1, 복사된 기록은 접힘
        Assert.Equal(new[] { "k2d6-agent", "k3-agent" }, entries.Select(e => e.Model).Distinct().OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OnlyWireFilesAreScanned()
    {
        WriteWire(Record(T));
        var other = Path.Combine(_sessions, "wd_proj_abc", "ses_1", "context.jsonl");
        File.WriteAllText(other, Record(T + 1));
        File.SetLastWriteTimeUtc(other, FileTimeUtc);
        Assert.Single(await Load());
    }

    [Fact]
    public async Task UnreadableWireFileIsSkipped()
    {
        var path = WriteWire("");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x41, 0x42 });
        File.SetLastWriteTimeUtc(path, FileTimeUtc);
        Assert.Empty(await Load());
    }

    [Fact]
    public async Task DesktopRootAndKimiHomeEnvAreScanned()
    {
        var desktopSessions = Path.Combine(_home, "Roaming", "kimi-desktop", "daimon-share", "daimon", "runtime", "kimi-code", "home", "sessions");
        var kimiHome = Path.Combine(_home, "custom-kimi");
        WriteWire(Record(T, model: "cli"));
        WriteWire(Record(T + 1, model: "desktop"), root: desktopSessions);
        WriteWire(Record(T + 2, model: "env"), root: Path.Combine(kimiHome, "sessions"));
        Func<string, string?> env = name => name == "KIMI_CODE_HOME" ? kimiHome : null;

        var roots = new KimiCodeUsageProvider().ResolveRoots(Ctx(env));
        Assert.Equal(new[]
        {
            Path.GetFullPath(_sessions),
            Path.GetFullPath(desktopSessions),
            Path.GetFullPath(Path.Combine(kimiHome, "sessions")),
        }, roots);

        var models = (await Load(env)).Select(e => e.Model).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[] { "cli", "desktop", "env" }, models);

        // 기본 루트를 가리키는 KIMI_CODE_HOME 은 중복 스캔하지 않는다
        Func<string, string?> dup = name => name == "KIMI_CODE_HOME" ? Path.Combine(_home, ".kimi-code") : null;
        Assert.Equal(2, new KimiCodeUsageProvider().ResolveRoots(Ctx(dup)).Count);
    }

    [Fact]
    public async Task PerModelBreakdownAndUnavailableCost()
    {
        WriteWire(string.Join("\n",
            Record(T, model: "k3-agent", input: 100, output: 200, read: 0, write: 0),
            Record(T + 1000, model: "k2d6-agent", scope: "session", input: 10, output: 20, read: 0, write: 0)));
        var entries = await Load();
        var models = entries.GroupBy(e => e.Model).ToDictionary(g => g.Key, g => g.Sum(e => e.Total));
        Assert.Equal(330, entries.Sum(e => e.Total));
        Assert.Equal(300, models["k3-agent"]);
        Assert.Equal(30, models["k2d6-agent"]);
        // 기록 비용도 가격표도 없음 → $0 이 아니라 "알 수 없음"
        Assert.All(entries, e => Assert.True(e.CostUnavailable
            || (e.ExplicitCost == null && ModelPricing.EstimatedCost(e.Model, e.Input, e.Output, e.CacheWrite, e.CacheRead) == null)));
    }

    [Fact]
    public async Task RecordsBeforeSinceAreExcluded()
    {
        var old = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        WriteWire(string.Join("\n", Record(old), Record(T)));
        var e = Assert.Single(await Load());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(T), e.Date);
    }
}
