using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>omp (oh-my-pi) 파싱 (원본 OmpUsageTests 재사용). 레이아웃: &lt;home&gt;/.omp/agent/sessions/&lt;cwd&gt;/&lt;ts&gt;_&lt;uuid&gt;.jsonl</summary>
public sealed class OmpProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FileTimeUtc = Now.AddMinutes(-30).UtcDateTime;

    private readonly string _base;
    private readonly string _home;
    private readonly string _sessions;
    private readonly string _project;

    private static readonly string[] SessionLines =
    {
        "{\"type\":\"session\",\"version\":3,\"id\":\"019f9552\",\"timestamp\":\"2026-07-03T01:00:00.000Z\",\"cwd\":\"/Users/x/Proj\"}",
        "{\"type\":\"model_change\",\"id\":\"aa01\",\"parentId\":null,\"timestamp\":\"2026-07-03T01:00:01.000Z\",\"model\":\"modal-k3/moonshotai/Kimi-K3\"}",
        "{\"type\":\"message\",\"id\":\"bb01\",\"parentId\":\"aa01\",\"timestamp\":\"2026-07-03T01:00:05.000Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}}",
        "{\"type\":\"message\",\"id\":\"cc01\",\"parentId\":\"bb01\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"hello\"}],\"model\":\"moonshotai/Kimi-K3\",\"usage\":{\"input\":100,\"output\":10,\"cacheRead\":600,\"cacheWrite\":40,\"totalTokens\":750,\"cost\":{\"input\":0.001,\"output\":0.002,\"cacheRead\":0.002,\"cacheWrite\":0.0,\"total\":0.005}}}}",
        "{\"type\":\"message\",\"id\":\"dd01\",\"parentId\":\"cc01\",\"timestamp\":\"2026-07-03T01:00:12.000Z\",\"message\":{\"role\":\"toolResult\",\"toolCallId\":\"t1\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}",
        "{\"type\":\"message\",\"id\":\"ee01\",\"parentId\":\"dd01\",\"timestamp\":\"2026-07-03T01:01:00.000Z\",\"message\":{\"role\":\"assistant\",\"content\":[],\"model\":\"modal/nvidia/GLM-5.2\",\"usage\":{\"input\":5,\"output\":7,\"cacheRead\":0,\"cacheWrite\":3,\"totalTokens\":15,\"cost\":{\"input\":0,\"output\":0,\"cacheRead\":0,\"cacheWrite\":0,\"total\":0}}}}",
        "{\"type\":\"custom\",\"customType\":\"tool_execution_start\",\"timestamp\":\"2026-07-03T01:01:05.000Z\"}",
    };

    private const string SessionFileName = "2026-07-03T01-00-00-000Z_019f9552.jsonl";

    public OmpProviderTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "cozy-omp-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_base, "home");
        _sessions = Path.Combine(_home, ".omp", "agent", "sessions");
        _project = Path.Combine(_sessions, "-Users-x-Proj");
        Directory.CreateDirectory(_project);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); }
        catch (Exception) { /* 무시 */ }
    }

    private string Write(string dir, string name, params string[] lines)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Join("\n", lines));
        File.SetLastWriteTimeUtc(path, FileTimeUtc);
        return path;
    }

    private ProviderContext Ctx(string? home = null, Func<string, string?>? env = null) => new()
    {
        Now = Now,
        Since = Since,
        Env = env ?? (_ => null),
        Home = home ?? _home,
        RoamingAppData = Path.Combine(_base, "Roaming"),
        LocalAppData = Path.Combine(_base, "Local"),
    };

    private static Task<IReadOnlyList<UsageEntry>> Load(ProviderContext ctx) => new OmpUsageProvider().LoadAsync(ctx);

    [Fact]
    public void Identity()
    {
        IUsageProvider p = new OmpUsageProvider();
        Assert.Equal("omp", p.Id);
        Assert.Equal("omp", p.DisplayName);
        Assert.True(p.IncludeModels);
    }

    [Fact]
    public async Task ParsesAssistantUsageMappingWithFileScopedIds()
    {
        Write(_project, SessionFileName, SessionLines);
        var entries = await Load(Ctx());
        Assert.Equal(2, entries.Count); // user/toolResult/custom 제외

        var cc = entries.Single(e => e.Id == "omp|" + SessionFileName + "|cc01");
        Assert.Equal("moonshotai/Kimi-K3", cc.Model);
        Assert.Equal(100, cc.Input);
        Assert.Equal(600, cc.CacheRead);
        Assert.Equal(40, cc.CacheWrite);
        Assert.Equal(10, cc.Output);
        Assert.Equal(750, cc.Total);
        Assert.Equal(0.005, cc.ExplicitCost);
        Assert.True(cc.CostIsEstimate);

        var ee = entries.Single(e => e.Id == "omp|" + SessionFileName + "|ee01");
        Assert.Equal("modal/nvidia/GLM-5.2", ee.Model);
        Assert.Equal(15, ee.Total);
        Assert.Equal(0.0, ee.ExplicitCost); // 명시적 0 유지

        Assert.Equal(765, entries.Sum(e => e.Total));
        Assert.Equal(0.005, entries.Sum(e => e.ExplicitCost ?? 0), 12);
    }

    [Fact]
    public async Task SkipsNonAssistantAndMalformedLines()
    {
        Write(_project, "broken.jsonl",
            "{\"type\":\"message\",\"id\":\"u1\",\"timestamp\":\"2026-07-03T01:00:05.000Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"what is my usage?\"}]}}",
            "{\"type\":\"message\",\"id\":\"a1\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"message\":{\"role\":\"assistant\",\"content\":[],\"model\":\"m\"}}",
            "{\"type\":\"message\",\"id\":\"a2\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":1,\"output\":1,\"cacheRead\":0,\"cacheWrite\":0}}}",
            "{\"type\":\"message\",\"id\":\"a3\",\"timestamp\":\"2026-07-03T01:00:20.000Z\",\"message\":{\"role\":\"assistant\",\"stopReason\":\"aborted\",\"usage\":{\"input\":9,\"output\":1,\"cacheRead\":0,\"cacheWrite\":0}}}",
            "{\"type\":\"compaction\",\"id\":\"cp1\",\"timestamp\":\"2026-07-03T01:00:30.000Z\",\"usage\":null}",
            "{\"type\":\"message\",\"id\":\"a4\",\"timestamp\":\"2026-07-03T01:00:40.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":1,");
        Assert.Empty(await Load(Ctx()));
    }

    [Fact]
    public async Task LineWithoutIdStillCountsAndDuplicatesCollapse()
    {
        Write(_project, "noid.jsonl",
            "{\"type\":\"message\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":10,\"output\":5,\"cacheRead\":0,\"cacheWrite\":0}}}");
        const string dup = "{\"type\":\"message\",\"id\":\"cc01\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":10,\"output\":5,\"cacheRead\":0,\"cacheWrite\":0}}}";
        Write(_project, "dup.jsonl", dup, dup);
        var entries = await Load(Ctx());
        Assert.Equal(2, entries.Count);
        Assert.StartsWith("omp|noid.jsonl|", entries.Single(e => e.Id != "omp|dup.jsonl|cc01").Id);
        Assert.Equal(15, entries.Single(e => e.Id == "omp|dup.jsonl|cc01").Total);
    }

    [Fact]
    public async Task SameMessageIdInDifferentFilesCountsTwice()
    {
        // omp 메시지 id 는 세션 안에서만 유일 → 파일 이름으로 범위 지정 (Pi 와 다름)
        const string line = "{\"type\":\"message\",\"id\":\"cc01\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":10,\"output\":5,\"cacheRead\":0,\"cacheWrite\":0}}}";
        Write(_project, "a.jsonl", line);
        Write(_project, "b.jsonl", line);
        var entries = await Load(Ctx());
        Assert.Equal(new[] { "omp|a.jsonl|cc01", "omp|b.jsonl|cc01" }, entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task BranchSummaryPartialGranularAndTotalOnly()
    {
        Write(_project, "mixed.jsonl",
            "{\"type\":\"branch_summary\",\"id\":\"bs01\",\"timestamp\":\"2026-07-03T01:04:00.000Z\",\"usage\":{\"input\":40,\"output\":6,\"cacheRead\":0,\"cacheWrite\":0,\"totalTokens\":46,\"cost\":{\"input\":0,\"output\":0,\"cacheRead\":0,\"cacheWrite\":0,\"total\":0}}}",
            "{\"type\":\"message\",\"id\":\"pg01\",\"timestamp\":\"2026-07-03T01:05:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":7}}}",
            "{\"type\":\"message\",\"id\":\"tt01\",\"timestamp\":\"2026-07-03T01:06:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"totalTokens\":42}}}");
        var byId = (await Load(Ctx())).ToDictionary(e => e.Id.Split('|').Last());

        Assert.Equal(46, byId["bs01"].Total);
        Assert.Equal("omp", byId["bs01"].Model); // envelope usage 에는 모델이 없다

        Assert.Equal(7, byId["pg01"].Input);
        Assert.Equal(0, byId["pg01"].Output + byId["pg01"].CacheWrite + byId["pg01"].CacheRead);

        var tt = byId["tt01"];
        Assert.Equal(new long[] { 42, 0, 0, 0 }, new[] { tt.Input, tt.Output, tt.CacheWrite, tt.CacheRead });
        Assert.True(tt.CostUnavailable);
    }

    [Fact]
    public async Task SubagentFilesAreIncluded()
    {
        Write(_project, SessionFileName, SessionLines);
        Write(Path.Combine(_project, "2026-07-03T01-00-00-000Z_019f9552"), "__advisor.jsonl",
            "{\"type\":\"message\",\"id\":\"ad01\",\"timestamp\":\"2026-07-03T01:02:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":7,\"output\":3,\"cacheRead\":0,\"cacheWrite\":0}}}");
        var entries = await Load(Ctx());
        Assert.Equal(3, entries.Count); // 부모 2 + 서브에이전트 1
        Assert.Equal(750 + 15 + 10, entries.Sum(e => e.Total));
    }

    [Fact]
    public async Task BridgeDirectoryBelowRootIsExcluded()
    {
        Write(_project, "s3.jsonl", SessionLines);
        Write(Path.Combine(_sessions, "bridge"), "converted.jsonl",
            "{\"type\":\"message\",\"id\":\"br01\",\"timestamp\":\"2026-07-03T01:03:00.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":9,\"output\":1,\"cacheRead\":0,\"cacheWrite\":0}}}");
        Assert.Equal(2, (await Load(Ctx())).Count);
    }

    [Fact]
    public async Task BridgeFolderAboveRootDoesNotExcludeEverything()
    {
        // 원본은 절대 경로 전체를 검사해 이 경우 0 이 됐다 — 포트는 루트 아래만 본다
        var home = Path.Combine(_base, "bridge", "home");
        var project = Path.Combine(home, ".omp", "agent", "sessions", "-Users-x-Proj");
        Write(project, SessionFileName, SessionLines);
        Assert.Equal(2, (await Load(Ctx(home))).Count);
    }

    [Fact]
    public async Task AgentDirEnvAddsRoot()
    {
        var agentDir = Path.Combine(_base, "omp-agent");
        Write(_project, SessionFileName, SessionLines);
        Write(Path.Combine(agentDir, "sessions", "proj"), "other.jsonl",
            "{\"type\":\"message\",\"id\":\"x1\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"message\":{\"role\":\"assistant\",\"usage\":{\"input\":1,\"output\":1}}}");
        var ctx = Ctx(env: name => name == "OMP_CODING_AGENT_DIR" ? agentDir : null);
        var roots = new OmpUsageProvider().ResolveRoots(ctx);
        Assert.Equal(new[] { Path.GetFullPath(_sessions), Path.GetFullPath(Path.Combine(agentDir, "sessions")) }, roots);
        Assert.Equal(3, (await Load(ctx)).Count);
    }

    [Fact]
    public async Task UnreadableFileInsideRootIsSkipped()
    {
        Write(_project, "ok.jsonl", SessionLines);
        var garbage = Path.Combine(_project, "garbage.jsonl");
        File.WriteAllBytes(garbage, new byte[] { 0xFF, 0xFE, 0x41, 0x42 });
        File.SetLastWriteTimeUtc(garbage, FileTimeUtc);
        Assert.Equal(2, (await Load(Ctx())).Count);
    }
}
