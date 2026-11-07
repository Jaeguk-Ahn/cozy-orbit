using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>원본 CopilotUsageTests 의 픽스처 (spec §4.9).</summary>
public sealed class CopilotProviderTests : IDisposable
{
    private const string Schema = """
        CREATE TABLE assistant_usage_events (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, turn_index INTEGER,
          agent_id TEXT, parent_tool_call_id TEXT, model TEXT NOT NULL, input_tokens INTEGER, output_tokens INTEGER,
          cache_read_tokens INTEGER, cache_write_tokens INTEGER, reasoning_tokens INTEGER, total_nano_aiu INTEGER,
          request_multiplier REAL, duration_ms INTEGER, time_to_first_token_ms INTEGER, inter_token_latency_ms INTEGER,
          initiator TEXT, api_endpoint TEXT, reasoning_effort TEXT, finish_reason TEXT, content_filter_triggered INTEGER,
          token_details_json TEXT, created_at TEXT DEFAULT (datetime('now')));
        """;

    private readonly string _root;
    private readonly string _home;

    public CopilotProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cozy-copilot-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    private string DefaultDb => Path.Combine(_home, ".copilot", "session-store.db");

    private static string EntryId(string db, long rowId) =>
        "copilot|" + Path.GetFullPath(db) + "|" + rowId.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset D(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private ProviderContext Ctx(DateTimeOffset since, Func<string, string?>? env = null) => new()
    {
        Now = D("2026-01-05T12:00:00Z"),
        Since = since,
        Env = env ?? (_ => null),
        Home = _home,
        RoamingAppData = Path.Combine(_root, "roaming"),
        LocalAppData = Path.Combine(_root, "local"),
    };

    private sealed record Row(long Id, string Model, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, string CreatedAt);

    private static void Exec(string dbPath, string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Seed(string db, params Row[] rows)
    {
        Exec(db, Schema);
        Insert(db, rows);
    }

    private static void Insert(string db, params Row[] rows)
    {
        if (rows.Length == 0) return;
        var sb = new StringBuilder("INSERT INTO assistant_usage_events VALUES ");
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(",\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"({r.Id}, 'session-1', 0, NULL, NULL, '{r.Model}', {r.Input}, {r.Output}, {r.CacheRead}, {r.CacheWrite}, {r.Reasoning}, 0, 1.0, 0, 0, 0, 'user', '/v1/messages', 'medium', 'stop', 0, NULL, '{r.CreatedAt}')");
        }
        sb.Append(';');
        Exec(db, sb.ToString());
    }

    [Fact]
    public async Task CachedPromptTokensAreNotCountedTwice()
    {
        Seed(DefaultDb, new Row(1, "claude-opus-5", 43_792, 443, 42_698, 904, 59, "2026-01-04T10:00:00.000Z"));

        var entries = await new CopilotUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal(190, e.Input);          // input_tokens - cache_read - cache_write
        Assert.Equal(42_698, e.CacheRead);
        Assert.Equal(904, e.CacheWrite);
        Assert.Equal(443, e.Output);         // reasoning 은 output 의 내역
        Assert.Equal(44_235, e.Total);
        Assert.Equal(EntryId(DefaultDb, 1), e.Id);
        Assert.Equal("claude-opus-5", e.Model);
        Assert.Null(e.ExplicitCost);
        Assert.False(e.CostUnavailable);     // 모델이 가격표에 있으면 추정 허용
    }

    [Fact]
    public async Task FullyCachedPromptKeepsZeroUncachedInput()
    {
        Seed(DefaultDb, new Row(1, "gpt-5.4-mini", 57_375, 35, 57_375, 0, 0, "2026-01-04T10:00:00.000Z"));

        var e = Assert.Single(await new CopilotUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z"))));

        Assert.Equal(0, e.Input);
        Assert.Equal(57_410, e.Total);
    }

    [Fact]
    public async Task ReadsBothStoredTimestampShapes()
    {
        Seed(DefaultDb,
            new Row(1, "claude-opus-5", 100, 10, 0, 0, 0, "2026-01-04T10:00:00.000Z"),
            new Row(2, "claude-opus-5", 200, 20, 0, 0, 0, "2026-01-04 11:00:00"));

        var entries = await new CopilotUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal(
            new[] { D("2026-01-04T10:00:00Z"), D("2026-01-04T11:00:00Z") },
            entries.Select(e => e.Date).OrderBy(d => d).ToArray());
    }

    [Fact]
    public async Task DayBoundaryCutoffKeepsSameDayIsoRows()
    {
        Seed(DefaultDb,
            new Row(1, "claude-opus-5", 100, 10, 0, 0, 0, "2026-01-04T00:00:01.000Z"),
            new Row(2, "claude-opus-5", 200, 20, 0, 0, 0, "2026-01-03T23:59:59.000Z"));
        var provider = new CopilotUsageProvider();

        Assert.Empty(await provider.LoadAsync(Ctx(D("2026-01-04T12:00:00Z"))));

        var wider = await provider.LoadAsync(Ctx(D("2026-01-04T00:00:00Z")));
        Assert.Equal(EntryId(DefaultDb, 1), Assert.Single(wider).Id);
    }

    [Fact]
    public async Task NegativeUtcOffsetRowIsNotDroppedByTheTextCutoff()
    {
        // 2026-01-04T01:00:00Z 이지만 텍스트상으로는 2026-01-03
        Seed(DefaultDb, new Row(1, "claude-opus-5", 100, 10, 0, 0, 0, "2026-01-03T20:00:00-05:00"));

        var entries = await new CopilotUsageProvider().LoadAsync(Ctx(D("2026-01-04T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal(D("2026-01-04T01:00:00Z"), e.Date);
    }

    [Fact]
    public async Task RowsWithoutTokensAreSkipped()
    {
        Seed(DefaultDb,
            new Row(1, "claude-opus-5", 0, 0, 0, 0, 0, "2026-01-04T10:00:00.000Z"),
            new Row(2, "claude-opus-5", 5, 1, 0, 0, 0, "2026-01-04T10:01:00.000Z"));

        var entries = await new CopilotUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal(EntryId(DefaultDb, 2), Assert.Single(entries).Id);
    }

    [Fact]
    public async Task NewRowsAppearOnTheNextLoadAndRecreatedStoreIsReplaced()
    {
        Seed(DefaultDb, new Row(1, "claude-opus-5", 100, 10, 0, 0, 0, "2026-01-04T10:00:00.000Z"));
        var provider = new CopilotUsageProvider();
        var ctx = Ctx(D("2026-01-01T00:00:00Z"));
        Assert.Single(await provider.LoadAsync(ctx));

        Insert(DefaultDb, new Row(2, "claude-opus-5", 300, 30, 0, 0, 0, "2026-01-04T11:00:00.000Z"));
        Assert.Equal(2, (await provider.LoadAsync(ctx)).Count);

        // 세션 데이터를 지우면 id 가 1 부터 다시 시작한다 — 이전 행과 섞이면 안 된다
        File.Delete(DefaultDb);
        Seed(DefaultDb, new Row(1, "claude-opus-5", 700, 70, 0, 0, 0, "2026-01-05T10:00:00.000Z"));
        var e = Assert.Single(await provider.LoadAsync(ctx));
        Assert.Equal(770, e.Total);
    }

    [Fact]
    public async Task TwoStoresWithTheSameRowIdsAreBothCounted()
    {
        var other = Path.Combine(_root, "other", "session-store.db");
        Seed(DefaultDb, new Row(1, "claude-opus-5", 100, 10, 0, 0, 0, "2026-01-04T10:00:00.000Z"));
        Seed(other, new Row(1, "gpt-5.4-mini", 400, 40, 0, 0, 0, "2026-01-04T11:00:00.000Z"));
        var env = Path.Combine(_home, ".copilot") + "," + Path.GetDirectoryName(other);
        var ctx = Ctx(D("2026-01-01T00:00:00Z"), name => name == "COPILOT_HOME" ? env : null);

        var entries = await new CopilotUsageProvider().LoadAsync(ctx);

        Assert.Equal(
            new[] { EntryId(DefaultDb, 1), EntryId(other, 1) }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(110 + 440, entries.Sum(e => e.Total));
    }

    [Fact]
    public async Task AcceptsADirectDatabasePathAsRoot()
    {
        var db = Path.Combine(_root, "elsewhere", "session-store.db");
        Seed(db, new Row(1, "claude-opus-5", 100, 10, 0, 0, 0, "2026-01-04T10:00:00.000Z"));
        var ctx = Ctx(D("2026-01-01T00:00:00Z"), name => name == "COPILOT_HOME" ? db : null);

        var entries = await new CopilotUsageProvider().LoadAsync(ctx);

        Assert.Equal(EntryId(db, 1), Assert.Single(entries).Id);
    }

    [Fact]
    public async Task UnreadableOrMissingDatabaseYieldsNothingWithoutThrowing()
    {
        var provider = new CopilotUsageProvider();
        Assert.Empty(await provider.LoadAsync(Ctx(D("2026-01-01T00:00:00Z"))));

        Directory.CreateDirectory(Path.GetDirectoryName(DefaultDb)!);
        File.WriteAllText(DefaultDb, "not-a-sqlite-database");
        Assert.Empty(await provider.LoadAsync(Ctx(D("2026-01-01T00:00:00Z"))));
    }

    [Fact]
    public void DefaultRootIsTheCopilotHome()
    {
        var roots = new CopilotUsageProvider().ResolveRoots(Ctx(D("2026-01-01T00:00:00Z")));
        Assert.Equal(new[] { Path.GetFullPath(Path.Combine(_home, ".copilot")) }, roots.ToArray());
    }

    [Theory]
    [InlineData("2026-01-04T10:00:00.000Z", "2026-01-04T10:00:00Z")]
    [InlineData("2026-01-04 11:00:00", "2026-01-04T11:00:00Z")]
    [InlineData("  2026-01-03T20:00:00-05:00 ", "2026-01-04T01:00:00Z")]
    public void ParsesCreatedAtShapes(string raw, string expected)
    {
        Assert.Equal(D(expected), CopilotUsageProvider.ParseCreatedAt(raw));
    }

    [Fact]
    public void RejectsShortCreatedAt()
    {
        Assert.Null(CopilotUsageProvider.ParseCreatedAt("2026-01-04"));
    }

    [Fact]
    public void DayCutoffIsTheUtcDayBeforeSince()
    {
        Assert.Equal("2026-01-03 00:00:00", CopilotUsageProvider.DayCutoff(D("2026-01-04T12:00:00Z")));
        Assert.Equal("2026-01-03 00:00:00", CopilotUsageProvider.DayCutoff(D("2026-01-04T00:00:00Z")));
    }
}
