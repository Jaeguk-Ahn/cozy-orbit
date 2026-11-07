using System.Text;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>원본 LocalUsageReaderTests / LocalUsageCacheTests 의 Codex 케이스 (spec §4.2). 픽스처: Fixtures/Codex/*.</summary>
public sealed class CodexProviderTests : IDisposable
{
    private static readonly DateTimeOffset Since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string ForkParentId = "00000000-0000-7000-8000-000000000001";
    private const string ForkChildId = "00000000-0000-7000-8000-000000000002";

    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    // ---------------------------------------------------------------- helpers

    private static string SessionsRel(string sub = "") =>
        sub.Length == 0 ? ".codex/sessions" : ".codex/sessions/" + sub;

    private string CopyFixture(TempDir tmp, string group, string name, string relDir, DateTime? mtimeUtc = null)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Usage", "Codex", group, name + ".jsonl");
        var dir = Path.Combine(tmp.Path, relDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, name + ".jsonl");
        File.Copy(src, dest, overwrite: true);
        File.SetLastWriteTimeUtc(dest, mtimeUtc ?? DateTime.UtcNow);
        return dest;
    }

    private void WriteRollout(string relPath, params string[] lines) =>
        _tmp.Write(SessionsRel(relPath), string.Join("\n", lines), DateTime.UtcNow);

    private static async Task<IReadOnlyList<UsageEntry>> LoadAsync(TempDir tmp, Dictionary<string, string>? env = null) =>
        await new CodexUsageProvider().LoadAsync(tmp.Context(DateTimeOffset.UtcNow, Since, env: env));

    private static List<long> TotalsByDate(IEnumerable<UsageEntry> entries) =>
        entries.OrderBy(e => e.Date).Select(e => e.Total).ToList();

    private static string Meta(string id, string ts) =>
        $"{{\"type\":\"session_meta\",\"timestamp\":\"{ts}\",\"payload\":{{\"id\":\"{id}\",\"session_id\":\"{id}\"}}}}";

    private static string ForkMeta(string id, string parentId, string ts) =>
        $"{{\"type\":\"session_meta\",\"timestamp\":\"{ts}\",\"payload\":{{\"id\":\"{id}\",\"session_id\":\"{parentId}\",\"forked_from_id\":\"{parentId}\",\"parent_thread_id\":\"{parentId}\",\"thread_source\":\"subagent\"}}}}";

    /// <summary>원본 codexStateLine: total_token_usage + last_token_usage</summary>
    private static string State(string ts, long cumIn, long cumOut, long lastIn, long lastOut, long? lastTotal = null)
    {
        long cumTotal = cumIn + cumOut;
        long lt = lastTotal ?? lastIn + lastOut;
        return $"{{\"type\":\"event_msg\",\"timestamp\":\"{ts}\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":{cumIn},\"cached_input_tokens\":0,\"output_tokens\":{cumOut},\"reasoning_output_tokens\":0,\"total_tokens\":{cumTotal}}},\"last_token_usage\":{{\"input_tokens\":{lastIn},\"cached_input_tokens\":0,\"output_tokens\":{lastOut},\"reasoning_output_tokens\":0,\"total_tokens\":{lt}}}}}}}}}";
    }

    // ---------------------------------------------------------------- basic parsing

    [Fact]
    public async Task TurnContextModelAndCachedInput()
    {
        WriteRollout("2026/07/02/rollout-1.jsonl",
            """{"timestamp":"2026-07-02T01:00:00.000Z","payload":{"type":"turn_context","model":"gpt-5.5-codex"}}""",
            """{"timestamp":"2026-07-02T01:01:00.000Z","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"cached_input_tokens":80,"output_tokens":7}}}}""");

        var entries = await LoadAsync(_tmp);

        var e = Assert.Single(entries);
        Assert.Equal("gpt-5.5-codex", e.Model);
        Assert.Equal(20, e.Input);      // 100 - 80
        Assert.Equal(80, e.CacheRead);
        Assert.Equal(7, e.Output);
        Assert.Equal(0, e.CacheWrite);
        Assert.Equal("codex|rollout-1.jsonl|0", e.Id); // 누적값 없음 → 위치 기반 id
    }

    [Fact]
    public async Task EntriesBeforeSinceAreExcluded()
    {
        WriteRollout("old/rollout-old.jsonl",
            """{"timestamp":"2025-12-31T23:00:00.000Z","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"cached_input_tokens":0,"output_tokens":7}}}}""",
            """{"timestamp":"2026-01-02T00:00:00.000Z","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":10,"cached_input_tokens":0,"output_tokens":1}}}}""");

        var entries = await LoadAsync(_tmp);

        Assert.Equal(new List<long> { 11 }, TotalsByDate(entries));
    }

    [Fact]
    public async Task ZeroContextTurnWithUnchangedCumulativeStaysZero()
    {
        WriteRollout("s.jsonl",
            Meta("session-a", "2026-07-29T01:00:00.000Z"),
            State("2026-07-29T01:00:01.000Z", 100, 10, 100, 10),
            // fork post-replay 형태: 누적 불변, last 구성 0, total 만 6742 → 0 유지 (#278 로 부풀리면 안 됨)
            State("2026-07-29T01:00:02.000Z", 100, 10, 0, 0, lastTotal: 6_742));

        var entries = await LoadAsync(_tmp);

        Assert.Equal(new List<long> { 110, 0 }, TotalsByDate(entries));
    }

    [Fact]
    public async Task TotalOnlyLastUsageCountsWhenItMatchesSessionTotal()
    {
        WriteRollout("s.jsonl",
            Meta("solo", "2026-09-04T01:29:58.000Z"),
            """{"type":"event_msg","timestamp":"2026-09-04T01:29:58.417Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":0,"cached_input_tokens":0,"output_tokens":0,"reasoning_output_tokens":0,"total_tokens":51293},"last_token_usage":{"input_tokens":0,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":0,"reasoning_output_tokens":0,"total_tokens":51293}}}}""");

        var e = Assert.Single(await LoadAsync(_tmp));
        Assert.Equal(51_293, e.Input);
        Assert.Equal(0, e.Output);
        Assert.Equal(0, e.CacheRead);
        Assert.True(e.CostUnavailable);
    }

    [Fact]
    public async Task TotalOnlyLastUsageCountsWhenCumulativeGrew()
    {
        WriteRollout("s.jsonl",
            Meta("grow", "2026-09-04T00:59:00.000Z"),
            """{"type":"event_msg","timestamp":"2026-09-04T01:00:00.000Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":0,"output_tokens":100,"reasoning_output_tokens":0,"total_tokens":1100},"last_token_usage":{"input_tokens":1000,"cached_input_tokens":0,"output_tokens":100,"reasoning_output_tokens":0,"total_tokens":1100}}}}""",
            """{"type":"event_msg","timestamp":"2026-09-04T01:01:00.000Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":0,"output_tokens":100,"reasoning_output_tokens":0,"total_tokens":6100},"last_token_usage":{"input_tokens":0,"cached_input_tokens":0,"output_tokens":0,"reasoning_output_tokens":0,"total_tokens":5000}}}}""");

        Assert.Equal(new List<long> { 1_100, 5_000 }, TotalsByDate(await LoadAsync(_tmp)));
    }

    [Fact]
    public async Task IdenticalSnapshotIsCountedOnce()
    {
        WriteRollout("s.jsonl",
            Meta("session-a", "2026-07-29T01:00:00.000Z"),
            State("2026-07-29T01:00:01.000Z", 100, 10, 100, 10),
            State("2026-07-29T01:00:05.000Z", 100, 10, 100, 10));

        Assert.Equal(new List<long> { 110 }, TotalsByDate(await LoadAsync(_tmp)));
    }

    [Fact]
    public async Task ForkOfForkReusesResolvedAncestorHistory()
    {
        var first = State("2026-07-30T01:00:01.000Z", 100, 10, 100, 10);
        var second = State("2026-07-30T01:00:02.000Z", 200, 20, 100, 10);
        var third = State("2026-07-30T01:00:03.000Z", 300, 30, 100, 10);
        var fourth = State("2026-07-30T01:00:04.000Z", 400, 40, 100, 10);
        WriteRollout("root.jsonl", Meta("root", "2026-07-30T01:00:00.000Z"), first, second);
        WriteRollout("child.jsonl", ForkMeta("child", "root", "2026-07-30T02:00:00.000Z"),
            Meta("root", "2026-07-30T02:00:00.001Z"), first, second, third);
        WriteRollout("grandchild.jsonl", ForkMeta("grandchild", "child", "2026-07-30T03:00:00.000Z"),
            Meta("child", "2026-07-30T03:00:00.001Z"), first, second, third, fourth);

        var entries = await LoadAsync(_tmp);

        Assert.Equal(new List<long> { 110, 110, 110, 110 }, TotalsByDate(entries));
        Assert.Equal(2, entries.Count(e => e.Id.StartsWith("codex|root|", StringComparison.Ordinal)));
        Assert.Equal(1, entries.Count(e => e.Id.StartsWith("codex|child|", StringComparison.Ordinal)));
        Assert.Equal(1, entries.Count(e => e.Id.StartsWith("codex|grandchild|", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ManualForkWithoutParentDropsReplayBurstByTiming()
    {
        // 누적값 없는 구형 replay + 부모 파일 없음 → 첫 이벤트와 1초 미만 간격 burst 제거
        static string Line(string ts, int output) =>
            $"{{\"type\":\"event_msg\",\"timestamp\":\"{ts}\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"last_token_usage\":{{\"input_tokens\":1000,\"cached_input_tokens\":200,\"output_tokens\":{output},\"total_tokens\":{1000 + output}}}}}}}}}";
        WriteRollout("child/rollout-child.jsonl",
            """{"type":"session_meta","timestamp":"2026-07-29T01:00:00.000Z","payload":{"id":"child","forked_from_id":"parent","parent_thread_id":"parent","thread_source":"user"}}""",
            Line("2026-07-29T01:00:00.010Z", 50),
            Line("2026-07-29T01:00:00.020Z", 51),
            Line("2026-07-29T01:00:03.000Z", 52));

        var e = Assert.Single(await LoadAsync(_tmp));
        Assert.Equal(52, e.Output);
    }

    [Fact]
    public async Task CodexHomeRootIsScanned()
    {
        _tmp.Write("alt-codex/sessions/rollout-x.jsonl",
            """{"timestamp":"2026-07-02T01:01:00.000Z","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"cached_input_tokens":80,"output_tokens":7}}}}""",
            DateTime.UtcNow);

        var entries = await LoadAsync(_tmp, new Dictionary<string, string> { ["CODEX_HOME"] = Path.Combine(_tmp.Path, "alt-codex") });

        Assert.Equal(107, Assert.Single(entries).Total); // 20 + 80 + 7
    }

    // ---------------------------------------------------------------- real fork fixtures

    [Fact]
    public async Task ForkChildAloneKeepsOnlyPostReplayUsage()
    {
        CopyFixture(_tmp, "CodexFork", "child", SessionsRel("2026/07/28"));

        var entries = await LoadAsync(_tmp);

        // child meta 뒤에 parent meta 와 8개 부모 token_count 가 재삽입됨 → 0 토큰 턴 + 새 턴만
        Assert.Equal(new List<long> { 0, 28_138 }, TotalsByDate(entries));
        Assert.All(entries, e => Assert.StartsWith($"codex|{ForkChildId}|", e.Id));
    }

    [Fact]
    public async Task ForkChildDoesNotDoubleCountParentReplay()
    {
        CopyFixture(_tmp, "CodexFork", "parent", SessionsRel("2026/07/13"));
        CopyFixture(_tmp, "CodexFork", "child", SessionsRel("2026/07/28"));

        var entries = await LoadAsync(_tmp);

        Assert.Equal(340_952, entries.Sum(e => e.Total));
        Assert.Equal(312_814, entries.Where(e => e.Id.StartsWith($"codex|{ForkParentId}|", StringComparison.Ordinal)).Sum(e => e.Total));
        var childOwned = entries.Where(e => e.Id.StartsWith($"codex|{ForkChildId}|", StringComparison.Ordinal)).ToList();
        Assert.Equal(new List<long> { 0, 28_138 }, TotalsByDate(childOwned));
        // zero-context post-replay 턴(last.total 6742)은 0 으로 남는다
        Assert.Contains(childOwned, e => e.Total == 0);
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task ParentOutsideWindowIsStillUsedForReplayMatching()
    {
        // 부모는 조회 창(mtime) 밖 + archived_sessions 에 있고 파일명에 id 가 없다 → probe 로 찾아 대조만 한다
        CopyFixture(_tmp, "CodexFork", "parent", ".codex/archived_sessions", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        CopyFixture(_tmp, "CodexFork", "child", SessionsRel("2026/07/28"));

        var entries = await LoadAsync(_tmp);

        Assert.Equal(new List<long> { 0, 28_138 }, TotalsByDate(entries));
        Assert.DoesNotContain(entries, e => e.Id.StartsWith($"codex|{ForkParentId}|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SiblingForksKeepIndependentPostReplayUsage()
    {
        CopyFixture(_tmp, "CodexFork", "parent", SessionsRel());
        CopyFixture(_tmp, "CodexFork", "child", SessionsRel());
        CopyFixture(_tmp, "CodexFork", "sibling", SessionsRel());

        var entries = await LoadAsync(_tmp);

        var forkTotals = entries.Select(e => e.Total).Where(t => t == 28_138 || t == 28_263).OrderBy(t => t).ToList();
        Assert.Equal(new List<long> { 28_138, 28_263 }, forkTotals);
        Assert.Equal(369_215, entries.Sum(e => e.Total));
    }

    [Fact]
    public async Task SameRolloutInActiveAndArchivedRootsCountsOnce()
    {
        CopyFixture(_tmp, "CodexFork", "parent", SessionsRel("2026/07/13"));
        CopyFixture(_tmp, "CodexFork", "parent", ".codex/archived_sessions");

        var entries = await LoadAsync(_tmp);

        Assert.Equal(312_814, entries.Sum(e => e.Total));
    }

    // ---------------------------------------------------------------- subagent fixtures

    [Theory]
    [InlineData("parent", "child", "00000000-0000-7000-8000-000000000002", 115_607L, new long[] { 22_992, 23_043, 23_062, 23_219, 23_291 })]
    [InlineData("parent-v145", "child-v145", "00000000-0000-7000-8000-000000000146", 106_583L, new long[] { 20_863, 21_175, 21_365, 21_458, 21_722 })]
    public async Task SubagentKeepsAllOwnUsageWithoutReplayPrefix(string parent, string child, string childId, long combined, long[] totals)
    {
        using var tmp = new TempDir();
        CopyFixture(tmp, "CodexSubagent", parent, SessionsRel());
        CopyFixture(tmp, "CodexSubagent", child, SessionsRel());

        var entries = await LoadAsync(tmp);

        Assert.Equal(totals.ToList(), entries.Select(e => e.Total).OrderBy(t => t).ToList());
        Assert.Equal(combined, entries.Sum(e => e.Total));
        Assert.Equal(2, entries.Count(e => e.Id.StartsWith($"codex|{childId}|", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("child", "00000000-0000-7000-8000-000000000002", "00000000-0000-7000-8000-000000000001", 23_062L, 23_291L)]
    [InlineData("child-v145", "00000000-0000-7000-8000-000000000146", "00000000-0000-7000-8000-000000000145", 21_458L, 21_722L)]
    public async Task SubagentChildKeepsFirstTurnWhenParentIsMissing(string name, string childId, string parentId, long first, long second)
    {
        using var tmp = new TempDir();
        var path = CopyFixture(tmp, "CodexSubagent", name, SessionsRel());

        var rollout = CodexUsageProvider.ParseRollout(path);
        Assert.Equal(childId, rollout.SessionId);
        Assert.Equal(parentId, rollout.ParentSessionId);
        Assert.True(rollout.IsSubagent);

        var entries = await LoadAsync(tmp);
        Assert.Equal(new List<long> { first, second }, TotalsByDate(entries));
        Assert.All(entries, e => Assert.StartsWith($"codex|{childId}|", e.Id));
    }

    // ---------------------------------------------------------------- metadata probe

    private string ProbeFile(byte[] bytes)
    {
        var path = Path.Combine(_tmp.Path, "probe-" + Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ProbeReadsSessionIdFromMetadataLineLongerThanChunk()
    {
        var meta = "{\"type\":\"session_meta\",\"timestamp\":\"2026-07-29T01:00:00.000Z\",\"payload\":{\"id\":\"parent-xl\",\"base_instructions\":\""
                   + new string('a', 200_000) + "\"}}";
        var path = ProbeFile(Encoding.UTF8.GetBytes(meta + "\n" + State("2026-07-29T01:00:01.000Z", 1, 1, 1, 1)));

        Assert.Equal((true, "parent-xl"), CodexUsageProvider.ProbeSessionId(path));
    }

    [Fact]
    public void ProbeFindsMetadataAfterLeadingRecordWithoutTrailingNewline()
    {
        var path = ProbeFile(Encoding.UTF8.GetBytes(
            "{\"type\":\"turn_context\",\"timestamp\":\"2026-07-29T01:00:00.000Z\",\"payload\":{}}\n"
            + Meta("parent-late", "2026-07-29T01:00:00.001Z")));

        Assert.Equal((true, "parent-late"), CodexUsageProvider.ProbeSessionId(path));
    }

    [Fact]
    public void ProbeStopsAtTokenCountBeforeSessionMeta()
    {
        var path = ProbeFile(Encoding.UTF8.GetBytes(
            State("2026-07-29T01:00:00.000Z", 1, 1, 1, 1) + "\n" + Meta("too-late", "2026-07-29T01:00:01.000Z")));

        Assert.Equal((true, (string?)null), CodexUsageProvider.ProbeSessionId(path));
    }

    [Fact]
    public void ProbeStopsAtInvalidUtf8BeforeSessionMeta()
    {
        var bytes = new List<byte> { 0xFF, (byte)'\n' };
        bytes.AddRange(Encoding.UTF8.GetBytes(Meta("wrong-parent", "2026-07-29T01:00:00.001Z")));
        var path = ProbeFile(bytes.ToArray());

        Assert.Equal((true, (string?)null), CodexUsageProvider.ProbeSessionId(path));
    }

    [Fact]
    public void ProbeRespectsByteLimit()
    {
        var meta = "{\"type\":\"session_meta\",\"timestamp\":\"2026-07-29T01:00:00.000Z\",\"payload\":{\"id\":\"parent-capped\",\"base_instructions\":\""
                   + new string('a', 3_000) + "\"}}";
        var bytes = Encoding.UTF8.GetBytes(meta);
        var path = ProbeFile(bytes);

        Assert.Null(CodexUsageProvider.ProbeSessionId(path, byteLimit: 1_024).SessionId);
        Assert.Equal("parent-capped", CodexUsageProvider.ProbeSessionId(path, byteLimit: bytes.Length).SessionId);
        Assert.Equal("parent-capped", CodexUsageProvider.ProbeSessionId(path).SessionId);
    }

    [Fact]
    public void FilenameHintRejectsDegenerateIds()
    {
        Assert.False(CodexUsageProvider.IsUsableFilenameHint(""));
        Assert.False(CodexUsageProvider.IsUsableFilenameHint("----"));
        Assert.False(CodexUsageProvider.IsUsableFilenameHint("ab"));
        Assert.True(CodexUsageProvider.IsUsableFilenameHint("00000000-0000-7000-8000-000000000001"));
    }
}
