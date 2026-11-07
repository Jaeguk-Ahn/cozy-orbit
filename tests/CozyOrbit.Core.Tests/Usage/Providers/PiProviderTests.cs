using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>Pi Agent 파싱 (원본 PiUsageTests 재사용). 레이아웃: &lt;home&gt;/.pi/agent/sessions/&lt;proj&gt;/*.jsonl</summary>
public sealed class PiProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FileTimeUtc = Now.AddHours(-1).UtcDateTime;

    private readonly string _home;
    private readonly string _sessions;

    public PiProviderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "cozy-pi-" + Guid.NewGuid().ToString("N"));
        _sessions = Path.Combine(_home, ".pi", "agent", "sessions");
        Directory.CreateDirectory(_sessions);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); }
        catch (Exception) { /* 무시 */ }
    }

    // ---------- fixtures ----------

    private static string Usage(long input = 0, long output = 0, long cacheWrite = 0, long cacheRead = 0, long? reasoning = null, long? totalTokens = null)
    {
        // 음수 기호가 문화권에 따라 달라지지 않도록 Invariant 로 직렬화
        var parts = new List<string>
        {
            FormattableString.Invariant($"\"input\":{input}"), FormattableString.Invariant($"\"output\":{output}"),
            FormattableString.Invariant($"\"cacheWrite\":{cacheWrite}"), FormattableString.Invariant($"\"cacheRead\":{cacheRead}"),
        };
        if (reasoning is { } r) parts.Add(FormattableString.Invariant($"\"reasoning\":{r}"));
        if (totalTokens is { } t) parts.Add(FormattableString.Invariant($"\"totalTokens\":{t}"));
        return "{" + string.Join(",", parts) + "}";
    }

    private static string Message(string id, string usage, string role = "assistant",
        string envelopeTimestamp = "2026-08-17T10:00:00.000Z", long? messageTimestampMs = null, string? stopReason = null)
    {
        var nested = "\"role\":\"" + role + "\",\"provider\":\"example\",\"model\":\"model-name\",\"content\":[],\"usage\":" + usage;
        if (messageTimestampMs is { } ms) nested += ",\"timestamp\":" + ms;
        if (stopReason != null) nested += ",\"stopReason\":\"" + stopReason + "\"";
        return "{\"type\":\"message\",\"id\":\"" + id + "\",\"parentId\":null,\"timestamp\":\"" + envelopeTimestamp + "\",\"message\":{" + nested + "}}";
    }

    private string Write(IEnumerable<string> lines, string? dir = null, string name = "session.jsonl")
    {
        var folder = dir ?? Path.Combine(_sessions, "proj");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, string.Join("\n", lines));
        File.SetLastWriteTimeUtc(path, FileTimeUtc);
        return path;
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

    private Task<IReadOnlyList<UsageEntry>> Load(Func<string, string?>? env = null) => new PiUsageProvider().LoadAsync(Ctx(env));

    // ---------- tests ----------

    [Fact]
    public void Identity()
    {
        IUsageProvider p = new PiUsageProvider();
        Assert.Equal("pi", p.Id);
        Assert.Equal("Pi", p.DisplayName);
        Assert.True(p.IncludeModels);
    }

    [Fact]
    public async Task SampleOutputIncludesReasoningAndPrefersMessageTimestamp()
    {
        // 원본 스펙 샘플: total 64 (reasoning 5 는 output 의 부분집합이라 더하지 않음)
        Write(new[]
        {
            "{\"type\":\"message\",\"id\":\"turn-1\",\"parentId\":null,\"timestamp\":\"2026-08-17T10:00:00.000Z\",\"message\":{\"role\":\"assistant\",\"provider\":\"example\",\"model\":\"model-name\",\"content\":[],\"usage\":{\"input\":10,\"output\":20,\"reasoning\":5,\"cacheWrite\":4,\"cacheRead\":30,\"totalTokens\":64},\"timestamp\":1786924799000}}",
        });
        var e = Assert.Single(await Load());
        Assert.Equal("turn-1", e.Id);
        Assert.Equal("model-name", e.Model);
        Assert.Equal(10, e.Input);
        Assert.Equal(20, e.Output);
        Assert.Equal(4, e.CacheWrite);
        Assert.Equal(30, e.CacheRead);
        Assert.Equal(64, e.Total);
        Assert.Equal(new DateTimeOffset(2026, 8, 16, 23, 59, 59, TimeSpan.Zero), e.Date);
        Assert.Equal(UsageTime.LocalDay(e.Date), e.LocalDay);
        Assert.True(e.CostIsEstimate);
        Assert.False(e.CostUnavailable);
    }

    [Fact]
    public async Task DirectMessageCompactionAndBranchSummaryUsageOnly()
    {
        var retained = Message("copied", Usage(input: 1_000));
        // retainedTail 은 복사된 문맥: 새 사용량이 아니다
        var compaction = "{\"type\":\"compaction\",\"id\":\"compact\",\"parentId\":\"assistant\",\"timestamp\":\"2026-08-17T10:00:02.000Z\",\"usage\":" +
                         Usage(input: 3) + ",\"retainedTail\":[" + retained + "]}";
        var branch = "{\"type\":\"branch_summary\",\"id\":\"summary\",\"parentId\":\"compact\",\"timestamp\":\"2026-08-17T10:00:03.000Z\",\"usage\":" +
                     Usage(output: 4) + "}";
        Write(new[]
        {
            Message("assistant", Usage(input: 1)),
            Message("tool-result", Usage(output: 2), role: "toolResult"), // role 필터 없음
            compaction,
            branch,
        });
        var entries = await Load();
        Assert.Equal(new[] { "assistant", "compact", "summary", "tool-result" }, entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(10, entries.Sum(e => e.Total));
        Assert.Equal("pi", entries.Single(e => e.Id == "compact").Model);
    }

    [Fact]
    public async Task SkipsAbortedAndErroredMessages()
    {
        Write(new[]
        {
            Message("aborted", Usage(input: 10), stopReason: "aborted"),
            Message("errored", Usage(input: 20), stopReason: "error"),
            Message("complete", Usage(input: 30), stopReason: "stop"),
        });
        Assert.Equal(new[] { "complete" }, (await Load()).Select(e => e.Id));
    }

    [Fact]
    public async Task FallbackMalformedAndPartialRecordsAreSafe()
    {
        Write(new[]
        {
            "{\"type\":\"message\",\"id\":\"total-only\",\"parentId\":null,\"timestamp\":\"2026-08-17T10:00:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"totalTokens\":77}}}",
            "{\"type\":\"message\",\"id\":\"null-fields\",\"parentId\":null,\"timestamp\":\"2026-08-17T10:00:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":null,\"output\":\"not-a-number\",\"totalTokens\":88}}}",
            Message("negative", Usage(input: -5, totalTokens: 99)),
            "{\"type\":\"message\",\"id\":\"no-usage\"}",
            "{\"type\":\"message\",\"id\":",
        });
        var byId = (await Load()).ToDictionary(e => e.Id);
        Assert.Equal(3, byId.Count);
        Assert.Equal(77, byId["total-only"].Total);
        Assert.True(byId["total-only"].CostUnavailable); // total 만 있으면 가격표 추정 금지
        Assert.Equal(88, byId["null-fields"].Total);
        Assert.Equal(0, byId["negative"].Total);          // 음수는 0 으로 (total 폴백 아님)
    }

    [Fact]
    public async Task OversizedFieldsClamp()
    {
        Write(new[]
        {
            "{\"type\":\"message\",\"id\":\"huge\",\"timestamp\":\"2026-08-17T10:00:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":1e30,\"output\":1e30,\"reasoning\":1e30,\"cacheWrite\":1e30,\"cacheRead\":1e30}}}",
        });
        var e = Assert.Single(await Load());
        Assert.Equal(JsonValues.MaxTokenValue, e.Input);
        Assert.Equal(JsonValues.MaxTokenValue, e.Output);
        Assert.Equal(JsonValues.MaxTokenValue * 4, e.Total);
    }

    [Fact]
    public async Task GlobalEnvelopeIdDedupAcrossRoots()
    {
        // 기본 루트 + PI_CODING_AGENT_SESSION_DIR 루트에 같은 엔트리(shared)가 복사됨
        var extra = Path.Combine(_home, "custom-sessions");
        var shared = Message("shared", Usage(input: 10));
        Write(new[] { shared, Message("branch-a", Usage(input: 20)) });
        Write(new[] { shared, Message("branch-b", Usage(input: 30)) }, dir: extra);
        var entries = await Load(name => name == "PI_CODING_AGENT_SESSION_DIR" ? extra : null);
        Assert.Equal(new[] { "branch-a", "branch-b", "shared" }, entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(60, entries.Sum(e => e.Total));
    }

    [Fact]
    public void SessionRootsCoverDefaultAndOverridesAndRemoveOverlap()
    {
        Func<string, string?> env = name => name switch
        {
            "PI_CODING_AGENT_DIR" => "~/custom-agent",
            "PI_CODING_AGENT_SESSION_DIR" => Path.Combine(_home, "custom-sessions"),
            _ => null,
        };
        var roots = new PiUsageProvider().ResolveRoots(Ctx(env));
        Assert.Equal(new[]
        {
            Path.GetFullPath(_sessions),
            Path.GetFullPath(Path.Combine(_home, "custom-agent", "sessions")),
            Path.GetFullPath(Path.Combine(_home, "custom-sessions")),
        }, roots);

        // 기본 루트 아래를 가리키는 값은 접힌다
        Func<string, string?> overlapping = name => name switch
        {
            "PI_CODING_AGENT_DIR" => Path.Combine(_home, ".pi", "agent"),
            "PI_CODING_AGENT_SESSION_DIR" => Path.Combine(_sessions, "project"),
            _ => null,
        };
        Assert.Equal(new[] { Path.GetFullPath(_sessions) }, new PiUsageProvider().ResolveRoots(Ctx(overlapping)));
    }

    [Fact]
    public async Task ModelFromEnvelopeOrMessageAndPerModelBreakdown()
    {
        var ompStyle = "{\"type\":\"message\",\"id\":\"omp-1\",\"timestamp\":\"2026-08-17T10:00:00.000Z\",\"model\":\"openrouter/stealth/ox-alpha\",\"message\":{\"role\":\"assistant\",\"content\":[],\"usage\":" +
                       Usage(input: 100, output: 200) + "}}";
        Write(new[] { ompStyle, Message("pi-1", Usage(input: 10, output: 20)) });
        var entries = await Load();
        Assert.Equal("openrouter/stealth/ox-alpha", entries.Single(e => e.Id == "omp-1").Model);
        Assert.Equal("model-name", entries.Single(e => e.Id == "pi-1").Model);

        var models = entries.GroupBy(e => e.Model).ToDictionary(g => g.Key, g => g.Sum(e => e.Total));
        Assert.Equal(330, entries.Sum(e => e.Total));
        Assert.Equal(300, models["openrouter/stealth/ox-alpha"]);
        Assert.Equal(30, models["model-name"]);
        // 모르는 모델 + 기록 비용 없음 → $0 이 아니라 "알 수 없음"
        Assert.All(entries, e => Assert.True(e.CostUnavailable
            || (e.ExplicitCost == null && ModelPricing.EstimatedCost(e.Model, e.Input, e.Output, e.CacheWrite, e.CacheRead) == null)));
    }

    [Fact]
    public async Task RecordedCostIsUsedAsEstimate()
    {
        Write(new[] { Message("c1", "{\"input\":1,\"output\":2,\"cost\":{\"total\":0.25}}") });
        var e = Assert.Single(await Load());
        Assert.Equal(0.25, e.ExplicitCost);
        Assert.True(e.CostIsEstimate);
    }
}
