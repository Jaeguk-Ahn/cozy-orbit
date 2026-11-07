using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

public class ClaudeCodeProviderTests
{
    private static string Line(string msgId, string reqId, string ts, long input, long output, long cw, long cr, string model = "claude-opus-4-8") =>
        $"{{\"type\":\"assistant\",\"requestId\":\"{reqId}\",\"timestamp\":\"{ts}\",\"message\":{{\"id\":\"{msgId}\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output},\"cache_creation_input_tokens\":{cw},\"cache_read_input_tokens\":{cr}}}}}}}";

    [Fact]
    public async Task ParsesAndDedupsStreamingDuplicates()
    {
        using var tmp = new TempDir();
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        tmp.Write(".claude/projects/proj/session.jsonl", string.Join("\n",
            Line("A", "R1", "2026-06-30T10:00:00.000Z", 100, 20, 0, 1000),
            Line("A", "R1", "2026-06-30T10:00:01.000Z", 100, 200, 0, 1000),
            "{\"type\":\"user\",\"message\":{\"content\":\"hi\"}}",
            "not json",
            Line("B", "R2", "2026-06-30T10:05:00.000Z", 1, 2, 3, 4)), now.UtcDateTime);

        var p = new ClaudeCodeUsageProvider();
        var entries = await p.LoadAsync(tmp.Context(now));
        Assert.Equal(2, entries.Count);
        var a = entries.Single(e => e.Id == "A|R1");
        Assert.Equal(1300, a.Total);
        Assert.Equal(DateTimeOffset.Parse("2026-06-30T10:00:00Z"), a.Date);
    }

    [Fact]
    public void ClampsHostileNumbers()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("x.jsonl", string.Join("\n",
            "{\"type\":\"assistant\",\"requestId\":\"r1\",\"timestamp\":\"2026-06-30T10:00:00.000Z\",\"message\":{\"id\":\"m1\",\"model\":\"claude\",\"usage\":{\"input_tokens\":1e30,\"output_tokens\":1e30,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}",
            "{\"type\":\"assistant\",\"requestId\":\"r2\",\"timestamp\":\"2026-06-30T10:00:00.000Z\",\"message\":{\"id\":\"m2\",\"model\":\"claude\",\"usage\":{\"input_tokens\":-5,\"output_tokens\":null,\"cache_read_input_tokens\":\"nope\"}}}"));
        var entries = ClaudeCodeUsageProvider.ParseFile(file);
        Assert.Equal(JsonValues.MaxTokenValue, entries.Single(e => e.Id == "m1|r1").Input);
        Assert.Equal(0, entries.Single(e => e.Id == "m2|r2").Total);
    }

    [Fact]
    public void CostStateLedgerIsDistributed()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("y.jsonl", string.Join("\n",
            Line("A", "1", "2026-06-30T10:00:00.000Z", 100, 0, 0, 0, "claude-opus-5[1m]"),
            Line("B", "2", "2026-06-30T10:01:00.000Z", 300, 0, 0, 0, "claude-opus-5[1m]"),
            "{\"type\":\"cost-state\",\"totalCostUSD\":1.0,\"modelUsage\":{\"claude-opus-5[1m]\":{\"costUSD\":0.4}}}",
            "{\"type\":\"cost-state\",\"totalCostUSD\":2.0,\"modelUsage\":{\"claude-opus-5[1m]\":{\"costUSD\":0.8}}}"));
        var entries = ClaudeCodeUsageProvider.ParseFile(file);
        Assert.Equal(0.8, entries.Sum(e => e.ExplicitCost ?? 0), 9);
        Assert.Equal(0.2, entries.Single(e => e.Id == "A|1").ExplicitCost!.Value, 9);
        Assert.All(entries, e => Assert.False(e.CostIsEstimate));
    }

    [Fact]
    public async Task CustomRootIsAdded()
    {
        using var tmp = new TempDir();
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        tmp.Write("D/work/claude/p/s.jsonl", Line("C", "R", "2026-06-30T11:00:00.000Z", 5, 5, 0, 0), now.UtcDateTime);
        var p = new ClaudeCodeUsageProvider();
        var entries = await p.LoadAsync(tmp.Context(now, customRoots: Path.Combine(tmp.Path, "D", "work", "claude")));
        Assert.Single(entries);
    }

    [Fact]
    public async Task FindsDesktopEmbeddedSessionsForEveryInstallKind()
    {
        using var tmp = new TempDir();
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var ts = "2026-06-30T11:00:00.000Z";
        // 일반 설치 / MSIX Claude-Data / MSIX 가상화 경로
        tmp.Write("AppData/Roaming/Claude/local-agent-mode-sessions/u1/u2/local_a/.claude/projects/p/s.jsonl", Line("D1", "R", ts, 1, 1, 0, 0), now.UtcDateTime);
        tmp.Write("AppData/Local/Claude-Data/claude_pzs8sxrjxfjjc/claude-code-sessions/u1/u2/local_b/.claude/projects/p/s.jsonl", Line("D2", "R", ts, 1, 1, 0, 0), now.UtcDateTime);
        tmp.Write("AppData/Local/Packages/Claude_pzs8sxrjxfjjc/LocalCache/Roaming/Claude/local-agent-mode-sessions/u1/local_c/.claude/projects/p/s.jsonl", Line("D3", "R", ts, 1, 1, 0, 0), now.UtcDateTime);
        // 패키지 이름이 다른 앱은 무시
        tmp.Write("AppData/Local/Packages/Other_x/LocalCache/Roaming/Claude/local-agent-mode-sessions/u1/.claude/projects/p/s.jsonl", Line("X", "R", ts, 1, 1, 0, 0), now.UtcDateTime);

        var entries = await new ClaudeCodeUsageProvider().LoadAsync(tmp.Context(now));
        Assert.Equal(new[] { "D1|R", "D2|R", "D3|R" }, entries.Select(e => e.Id).OrderBy(x => x));
    }
}
