using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>Grok CLI 파싱 (원본 GrokUsageTests 픽스처 재사용). 레이아웃: &lt;home&gt;/.grok/sessions/&lt;cwd&gt;/&lt;sid&gt;/updates.jsonl</summary>
public sealed class GrokProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FileTimeUtc = Now.AddHours(-1).UtcDateTime;

    private readonly string _home;
    private readonly string _sessions;

    public GrokProviderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "cozy-grok-" + Guid.NewGuid().ToString("N"));
        _sessions = Path.Combine(_home, ".grok", "sessions");
        Directory.CreateDirectory(_sessions);
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    // ---------- fixtures ----------

    private const string ChunkLine =
        "{\"timestamp\":1785000000,\"method\":\"_x.ai/session/update\",\"params\":{\"sessionId\":\"s1\",\"update\":{\"sessionUpdate\":\"agent_message_chunk\",\"content\":{\"type\":\"text\",\"text\":\"hi\"}},\"_meta\":{\"totalTokens\":100,\"eventId\":\"e0\",\"agentTimestampMs\":1785000000000,\"chunkId\":0}}}";

    /// <summary>원본 turnLine 빌더 (camelCase durable wire: inputTokens 는 캐시 읽기 포함)</summary>
    private static string TurnLine(
        string promptId, long input = 41_203, long output = 812, long cachedRead = 38_400, long total = 42_015,
        long? costTicks = 12_000_000_000, bool costIsPartial = false, bool usageIsIncomplete = false,
        string? model = "grok-build-1", long? envelopeSeconds = 1_785_000_010, long? agentTimestampMs = 1_785_000_010_000,
        bool isReplay = false)
    {
        var usage = new List<string>
        {
            "\"inputTokens\":" + input, "\"outputTokens\":" + output, "\"totalTokens\":" + total,
            "\"cachedReadTokens\":" + cachedRead, "\"reasoningTokens\":260", "\"modelCalls\":3", "\"numTurns\":1",
        };
        if (costTicks is { } ticks) usage.Add("\"costUsdTicks\":" + ticks);
        if (costIsPartial) usage.Add("\"costIsPartial\":true");
        if (usageIsIncomplete) usage.Add("\"usageIsIncomplete\":true");
        if (model != null)
            usage.Add("\"modelUsage\":{\"" + model + "\":{\"inputTokens\":" + input + ",\"outputTokens\":" + output +
                      ",\"totalTokens\":" + total + ",\"cachedReadTokens\":" + cachedRead + "}}");
        var meta = new List<string> { "\"totalTokens\":" + total, "\"eventId\":\"ev-" + promptId + "\"", "\"promptId\":\"" + promptId + "\"" };
        if (agentTimestampMs is { } ms) meta.Add("\"agentTimestampMs\":" + ms);
        if (isReplay) meta.Add("\"isReplay\":true");
        var ts = envelopeSeconds is { } s ? "\"timestamp\":" + s + "," : "";
        return "{" + ts + "\"method\":\"_x.ai/session/update\",\"params\":{\"sessionId\":\"s1\",\"update\":{\"sessionUpdate\":\"turn_completed\",\"prompt_id\":\"" +
               promptId + "\",\"stop_reason\":\"end_turn\",\"usage\":{" + string.Join(",", usage) + "}},\"_meta\":{" +
               string.Join(",", meta) + "}}}";
    }

    private string WriteSession(string id, IEnumerable<string> lines, string? sessionKind = null, bool summary = true, string? root = null)
    {
        var dir = Path.Combine(root ?? _sessions, "cwd-group", id);
        Directory.CreateDirectory(dir);
        var updates = Path.Combine(dir, "updates.jsonl");
        File.WriteAllText(updates, string.Join("\n", lines));
        File.SetLastWriteTimeUtc(updates, FileTimeUtc);
        if (summary) WriteSummary(dir, sessionKind);
        return updates;
    }

    private static void WriteSummary(string sessionDir, string? sessionKind)
    {
        var text = "{\"session_summary\":\"x\"" + (sessionKind != null ? ",\"session_kind\":\"" + sessionKind + "\"" : "") + "}";
        File.WriteAllText(Path.Combine(sessionDir, "summary.json"), text);
    }

    private ProviderContext Ctx(Func<string, string?>? env = null, DateTimeOffset? since = null) => new()
    {
        Now = Now,
        Since = since ?? Since,
        Env = env ?? (_ => null),
        Home = _home,
        RoamingAppData = Path.Combine(_home, "Roaming"),
        LocalAppData = Path.Combine(_home, "Local"),
    };

    private Task<IReadOnlyList<UsageEntry>> Load(GrokUsageProvider? provider = null, Func<string, string?>? env = null) =>
        (provider ?? new GrokUsageProvider()).LoadAsync(Ctx(env));

    // ---------- token mapping ----------

    [Fact]
    public void Identity()
    {
        var p = new GrokUsageProvider();
        Assert.Equal("grok", p.Id);
        Assert.Equal("Grok", p.DisplayName);
        Assert.False(((IUsageProvider)p).IncludeModels);
    }

    [Fact]
    public async Task SampleTurnMapsTokensAndCost()
    {
        WriteSession("s1", new[] { ChunkLine, TurnLine("p-1") });
        var entries = await Load();
        var e = Assert.Single(entries); // 청크 라인은 집계 대상이 아니다
        Assert.Equal("grok|p-1", e.Id);
        Assert.Equal(2_803, e.Input);        // 41203 − 38400
        Assert.Equal(38_400, e.CacheRead);
        Assert.Equal(812, e.Output);         // reasoning 은 output 안
        Assert.Equal(0, e.CacheWrite);
        Assert.Equal(42_015, e.Total);
        Assert.Equal("grok-build-1", e.Model);
        Assert.NotNull(e.ExplicitCost);
        Assert.Equal(1.20, e.ExplicitCost!.Value, 9);
        Assert.False(e.CostIsEstimate);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_785_000_010_000), e.Date);
        Assert.Equal(UsageTime.LocalDay(e.Date), e.LocalDay);
    }

    [Fact]
    public async Task SnakeCaseInputIsNotCacheAdjustedAgain()
    {
        const string line = "{\"timestamp\":1785000020,\"method\":\"_x.ai/session/update\",\"params\":{\"sessionId\":\"s2\",\"update\":{\"sessionUpdate\":\"turn_completed\",\"prompt_id\":\"p-snake\",\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":60,\"output_tokens\":10,\"total_tokens\":110,\"cached_read_tokens\":40}},\"_meta\":{\"eventId\":\"ev-snake\",\"agentTimestampMs\":1785000020000}}}";
        WriteSession("s2", new[] { line });
        var e = Assert.Single(await Load());
        Assert.Equal(60, e.Input);
        Assert.Equal(40, e.CacheRead);
        Assert.Equal(10, e.Output);
        Assert.Equal(110, e.Total);
    }

    [Fact]
    public async Task NullTokenFieldsFallBackToSnakeCase()
    {
        const string line = "{\"timestamp\":1785000030,\"method\":\"_x.ai/session/update\",\"params\":{\"sessionId\":\"s5\",\"update\":{\"sessionUpdate\":\"turn_completed\",\"prompt_id\":\"p-null\",\"stop_reason\":\"end_turn\",\"usage\":{\"inputTokens\":null,\"input_tokens\":60,\"outputTokens\":null,\"output_tokens\":10,\"cachedReadTokens\":40,\"totalTokens\":110}},\"_meta\":{\"eventId\":\"ev-null\",\"agentTimestampMs\":1785000030000}}}";
        WriteSession("nulls", new[] { line });
        var e = Assert.Single(await Load());
        Assert.Equal(60, e.Input);
        Assert.Equal(10, e.Output);
        Assert.Equal(40, e.CacheRead);
        Assert.Equal(110, e.Total);
    }

    [Fact]
    public async Task MultipleTurnsAggregateAndCostOnlyFromTicks()
    {
        WriteSession("s3", new[]
        {
            ChunkLine, TurnLine("p-1"), ChunkLine,
            TurnLine("p-2", input: 100, output: 20, cachedRead: 0, total: 120, costTicks: null),
        });
        var entries = await Load();
        Assert.Equal(2, entries.Count);
        Assert.Equal(42_015 + 120, entries.Sum(e => e.Total));
        Assert.Null(entries.Single(e => e.Id == "grok|p-2").ExplicitCost);
    }

    [Fact]
    public async Task CacheReadIsClampedAndResidualGoesToOutput()
    {
        WriteSession("clamp", new[] { TurnLine("p-clamp", input: 1_000, output: 50, cachedRead: 1_200, total: 1_050, costTicks: null) });
        WriteSession("residual", new[] { TurnLine("p-res", input: 500, output: 10, cachedRead: 100, total: 700, costTicks: null) });
        var entries = await Load();
        var clamp = entries.Single(e => e.Id == "grok|p-clamp");
        Assert.Equal(0, clamp.Input);
        Assert.Equal(1_000, clamp.CacheRead);
        Assert.Equal(1_050, clamp.Total);
        var res = entries.Single(e => e.Id == "grok|p-res");
        Assert.Equal(400, res.Input);
        Assert.Equal(100, res.CacheRead);
        Assert.Equal(200, res.Output); // 10 + (700 − 510)
        Assert.Equal(700, res.Total);
    }

    [Fact]
    public async Task ZeroUsageTurnAndMissingModelUsage()
    {
        WriteSession("zero", new[] { TurnLine("p-zero", input: 0, output: 0, cachedRead: 0, total: 0, costTicks: null) });
        WriteSession("nomodel", new[] { TurnLine("p-nm", model: null) });
        var e = Assert.Single(await Load());
        Assert.Equal("grok|p-nm", e.Id);
        Assert.Equal("grok", e.Model);
        Assert.Equal(42_015, e.Total);
    }

    [Fact]
    public async Task UntrustworthyCostsAreDropped()
    {
        WriteSession("partial", new[] { TurnLine("p-partial", costIsPartial: true) });
        WriteSession("incomplete", new[] { TurnLine("p-incomplete", usageIsIncomplete: true) });
        var entries = await Load();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Null(e.ExplicitCost));
    }

    // ---------- double counting ----------

    [Fact]
    public async Task ReplayLinesAreNotCountedTwice()
    {
        WriteSession("s4", new[] { TurnLine("p-1"), TurnLine("p-1", isReplay: true) });
        WriteSession("s4-replay", new[] { TurnLine("p-live"), TurnLine("p-replayed", isReplay: true) });
        var ids = (await Load()).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "grok|p-1", "grok|p-live" }, ids);
    }

    [Fact]
    public async Task ForkedSessionCopyDoesNotDoubleCount()
    {
        WriteSession("parent", new[] { TurnLine("p-1") });
        WriteSession("child-fork", new[] { TurnLine("p-1") }, sessionKind: "fork");
        var e = Assert.Single(await Load());
        Assert.Equal(42_015, e.Total);
    }

    [Fact]
    public async Task SubagentSessionsAreSkippedButUserSessionsKept()
    {
        WriteSession("main", new[] { TurnLine("p-main") });
        WriteSession("sub", new[] { TurnLine("p-sub") }, sessionKind: "subagent");
        WriteSession("sub2", new[] { TurnLine("p-sub2") }, sessionKind: "subagent_fork");
        WriteSession("sub3", new[] { TurnLine("p-sub3") }, sessionKind: "subagent_resume");
        WriteSession("wt", new[] { TurnLine("p-wt") }, sessionKind: "worktree");
        WriteSession("nosummary", new[] { TurnLine("p-nosum") }, summary: false);
        var ids = (await Load()).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "grok|p-main", "grok|p-nosum", "grok|p-wt" }, ids);
    }

    [Fact]
    public async Task LateWrittenSessionKindStopsCountingOnNextLoad()
    {
        var provider = new GrokUsageProvider();
        var updates = WriteSession("sub-late", new[] { TurnLine("p-sub-late") }, summary: false);
        var before = await Load(provider);
        Assert.Equal(new[] { "grok|p-sub-late" }, before.Select(e => e.Id));
        // updates.jsonl 은 그대로(파싱 캐시 히트), summary 만 나중에 subagent 로 기록됨
        WriteSummary(Path.GetDirectoryName(updates)!, "subagent");
        var after = await Load(provider);
        Assert.Empty(after);
    }

    // ---------- timestamps ----------

    [Fact]
    public async Task AgentTimestampWinsOverEnvelopeWriteTime()
    {
        WriteSession("forked", new[] { TurnLine("p-old", envelopeSeconds: 1_785_600_000, agentTimestampMs: 1_785_000_010_000) });
        var e = Assert.Single(await Load());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_000_010), e.Date);
    }

    [Fact]
    public async Task EnvelopeSecondsUsedWhenAgentTimestampMissing()
    {
        WriteSession("ts", new[] { TurnLine("p-ts", envelopeSeconds: 1_785_000_010, agentTimestampMs: null) });
        var e = Assert.Single(await Load());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_000_010), e.Date);
    }

    [Fact]
    public async Task EntriesBeforeSinceAreExcluded()
    {
        // 2026-06-01 의 턴 (Since = 2026-07-01 이전)
        var old = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        WriteSession("old", new[] { TurnLine("p-old", agentTimestampMs: old), TurnLine("p-new") });
        var e = Assert.Single(await Load());
        Assert.Equal("grok|p-new", e.Id);
    }

    // ---------- files / roots ----------

    [Fact]
    public async Task OnlyUpdatesFileIsRead()
    {
        var updates = WriteSession("s-cache", new[] { ChunkLine, TurnLine("p-1") });
        var dir = Path.GetDirectoryName(updates)!;
        File.WriteAllText(Path.Combine(dir, "chat_history.jsonl"), TurnLine("p-should-not-count"));
        File.WriteAllText(Path.Combine(dir, "events.jsonl"), TurnLine("p-events"));
        Assert.Equal(new[] { "grok|p-1" }, (await Load()).Select(e => e.Id));
    }

    [Fact]
    public async Task FilesNotModifiedSinceWindowStartAreSkipped()
    {
        var updates = WriteSession("stale", new[] { TurnLine("p-1") });
        File.SetLastWriteTimeUtc(updates, Since.AddDays(-3).UtcDateTime);
        Assert.Empty(await Load());
    }

    [Fact]
    public async Task GrokHomeReplacesDefaultRoot()
    {
        var grokHome = Path.Combine(_home, "custom-grok");
        var customSessions = Path.Combine(grokHome, "sessions");
        WriteSession("default", new[] { TurnLine("p-default") });
        WriteSession("custom", new[] { TurnLine("p-custom") }, root: customSessions);
        Func<string, string?> env = name => name == "GROK_HOME" ? grokHome : null;

        var roots = new GrokUsageProvider().ResolveRoots(Ctx(env));
        Assert.Equal(Path.GetFullPath(customSessions), Assert.Single(roots));
        Assert.Equal(new[] { "grok|p-custom" }, (await Load(env: env)).Select(e => e.Id));

        var defaultRoots = new GrokUsageProvider().ResolveRoots(Ctx());
        Assert.Equal(Path.GetFullPath(_sessions), Assert.Single(defaultRoots));
    }

    [Fact]
    public async Task MissingRootIsSilent()
    {
        Directory.Delete(_sessions, recursive: true);
        Assert.Empty(await Load());
    }
}
