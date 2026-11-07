using System.Globalization;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>원본 LocalAdditionalUsageTests 의 OpenCode 픽스처 (spec §4.5).</summary>
public sealed class OpenCodeProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string _home;

    public OpenCodeProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cozy-opencode-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    private string DataDir => Path.Combine(_home, ".local", "share", "opencode");
    private string DefaultDb => Path.Combine(DataDir, "opencode.db");

    private static DateTimeOffset D(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private ProviderContext Ctx(DateTimeOffset since, Func<string, string?>? env = null) => new()
    {
        Now = D("2026-01-02T12:00:00Z"),
        Since = since,
        Env = env ?? (_ => null),
        Home = _home,
        RoamingAppData = Path.Combine(_root, "roaming"),
        LocalAppData = Path.Combine(_root, "local"),
    };

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

    private static void Near(double expected, double? actual)
    {
        Assert.True(actual.HasValue, "expected a cost value");
        Assert.True(Math.Abs(expected - actual.GetValueOrDefault()) < 1e-9, $"expected {expected}, got {actual}");
    }

    [Fact]
    public async Task V1MessagePreservesReportedTotalAndCost()
    {
        Exec(DefaultDb, """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('msg-1', 'session-1', 1767312000000,
              '{"id":"msg-1","sessionID":"session-1","providerID":"anthropic","modelID":"claude-sonnet-4-20250514","time":{"created":1767312000000},"tokens":{"input":100,"output":50,"total":250,"cache":{"read":10,"write":20}},"cost":0.25}');
            """);

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal("opencode|msg-1", e.Id);
        Assert.Equal("claude-sonnet-4-20250514", e.Model);
        Assert.Equal(100, e.Input);
        Assert.Equal(120, e.Output); // 50 + 분류되지 않은 70
        Assert.Equal(20, e.CacheWrite);
        Assert.Equal(10, e.CacheRead);
        Assert.Equal(250, e.Total);
        Near(0.25, e.ExplicitCost);
        Assert.True(e.CostUnavailable); // total > parts
        Assert.Equal(D("2026-01-02T00:00:00Z"), e.Date);
    }

    [Fact]
    public async Task FallsBackToLegacyDatabaseWithoutTimeCreatedColumn()
    {
        Exec(DefaultDb, """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('legacy-1', 'session-1',
              '{"id":"legacy-1","sessionID":"session-1","providerID":"openai","modelID":"gpt-5","time":{"created":1767312000000},"tokens":{"input":7,"output":3,"cache":{"read":2,"write":1}},"cost":0}');
            """);

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal(13, e.Total);
        Near(0, e.ExplicitCost); // 기록된 0 은 그대로 유지
    }

    [Fact]
    public async Task ReadsV2SessionMessagesAndMergesV1()
    {
        Exec(DefaultDb, """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            CREATE TABLE session_message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, type TEXT NOT NULL, seq INTEGER NOT NULL,
              time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL, data TEXT NOT NULL);
            CREATE INDEX session_message_time_created_idx ON session_message (time_created);
            INSERT INTO message VALUES ('msg-v1', 'session-v1', 1767312000000,
              '{"id":"msg-v1","sessionID":"session-v1","providerID":"anthropic","modelID":"claude-sonnet-4-20250514","time":{"created":1767312000000},"tokens":{"input":100,"output":50,"total":180,"cache":{"read":10,"write":20}},"cost":0.25}');
            INSERT INTO session_message VALUES ('msg-v2', 'session-v2', 'assistant', 1, 1767312000000, 1767312000000,
              '{"model":{"id":"gpt-5","providerID":"openai"},"time":{"created":1767312000000},"tokens":{"input":100,"output":50,"reasoning":30,"cache":{"read":10,"write":20}},"cost":0.5}');
            INSERT INTO session_message VALUES ('msg-user', 'session-v2', 'user', 2, 1767312000000, 1767312000000,
              '{"time":{"created":1767312000000},"text":"not usage"}');
            """);

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal(2, entries.Count);
        var v1 = entries.Single(e => e.Id == "opencode|msg-v1");
        Assert.Equal(180, v1.Total);
        Near(0.25, v1.ExplicitCost);

        var v2 = entries.Single(e => e.Id == "opencode|msg-v2");
        Assert.Equal("gpt-5", v2.Model);
        Assert.Equal(100, v2.Input);
        Assert.Equal(80, v2.Output); // V2 는 reasoning 을 별도 저장
        Assert.Equal(10, v2.CacheRead);
        Assert.Equal(20, v2.CacheWrite);
        Assert.Equal(210, v2.Total);
        Near(0.5, v2.ExplicitCost);
        Assert.False(v2.CostUnavailable);
    }

    [Fact]
    public async Task V1AndV2CopiesOfTheSameMessageCollapse()
    {
        Exec(DefaultDb, """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            CREATE TABLE session_message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, type TEXT NOT NULL, seq INTEGER NOT NULL,
              time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('same', 's', 1767312000000,
              '{"id":"same","providerID":"openai","modelID":"gpt-5","time":{"created":1767312000000},"tokens":{"input":100,"output":50,"cache":{"read":0,"write":0}}}');
            INSERT INTO session_message VALUES ('same', 's', 'assistant', 1, 1767312000000, 1767312000000,
              '{"model":{"id":"gpt-5","providerID":"openai"},"tokens":{"input":100,"output":50,"reasoning":30,"cache":{"read":0,"write":0}}}');
            """);

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal(180, e.Total); // keep-max: 더 큰 V2 (reasoning 포함)
    }

    [Fact]
    public async Task ReadsLegacyStorageMessageFiles()
    {
        var dir = Path.Combine(DataDir, "storage", "message", "ses_1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "msg_file.json"),
            """{"sessionID":"ses_1","providerID":"anthropic","modelID":"claude-sonnet-4-20250514","time":{"created":1767312000000},"tokens":{"input":"40","output":2,"cache":{"read":0,"write":0}}}""");
        // 사용자 메시지 (tokens 없음) 는 무시
        File.WriteAllText(Path.Combine(dir, "msg_user.json"),
            """{"id":"u","sessionID":"ses_1","time":{"created":1767312000000}}""");

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal("opencode|msg_file", e.Id); // id 가 없으면 파일 이름
        Assert.Equal(40, e.Input);               // 숫자 문자열도 허용 (B 계열)
        Assert.Equal(42, e.Total);
    }

    [Fact]
    public async Task FindsChannelDatabaseWhenStandardIsMissing()
    {
        Exec(Path.Combine(DataDir, "opencode-beta.db"), """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('m', 's', 1767312000000,
              '{"id":"m","providerID":"openai","modelID":"gpt-5","time":{"created":1767312000000},"tokens":{"input":5,"output":5}}');
            """);

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal(10, Assert.Single(entries).Total);
        Assert.Equal(Path.Combine(DataDir, "opencode-beta.db"), OpenCodeUsageProvider.PreferredDatabase(DataDir));
    }

    [Fact]
    public async Task RecordsBeforeSinceAreExcluded()
    {
        Exec(DefaultDb, """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('old', 's', 1767312000000,
              '{"id":"old","providerID":"openai","modelID":"gpt-5","time":{"created":1767312000000},"tokens":{"input":5,"output":5}}');
            """);

        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-03T00:00:00Z")));

        Assert.Empty(entries);
    }

    [Fact]
    public async Task EnvironmentDataDirReplacesDefaults()
    {
        var custom = Path.Combine(_root, "custom-opencode");
        Exec(Path.Combine(custom, "opencode.db"), """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('env', 's', 1767312000000,
              '{"id":"env","providerID":"openai","modelID":"gpt-5","time":{"created":1767312000000},"tokens":{"input":1,"output":1}}');
            """);
        // 기본 위치의 DB 는 env 가 있으면 읽지 않는다
        Exec(DefaultDb, """
            CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL, data TEXT NOT NULL);
            INSERT INTO message VALUES ('default', 's', 1767312000000,
              '{"id":"default","providerID":"openai","modelID":"gpt-5","time":{"created":1767312000000},"tokens":{"input":1,"output":1}}');
            """);
        var ctx = Ctx(D("2026-01-01T00:00:00Z"), name => name == "OPENCODE_DATA_DIR" ? custom : null);

        var provider = new OpenCodeUsageProvider();
        var roots = provider.ResolveRoots(ctx);
        var entries = await provider.LoadAsync(ctx);

        Assert.Equal(new[] { Path.GetFullPath(custom) }, roots.ToArray());
        Assert.Equal("opencode|env", Assert.Single(entries).Id);
    }

    [Fact]
    public void DefaultRootsIncludeXdgAndLocalAppData()
    {
        var roots = new OpenCodeUsageProvider().ResolveRoots(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal(new[]
        {
            Path.GetFullPath(DataDir),
            Path.GetFullPath(Path.Combine(_root, "local", "opencode")),
        }, roots.ToArray());
    }

    [Fact]
    public async Task MissingDataDirectoryYieldsNothing()
    {
        var entries = await new OpenCodeUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));
        Assert.Empty(entries);
    }
}
