using System.Globalization;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>원본 AsideUsageTests 의 합성 픽스처 (spec §4.13).</summary>
public sealed class AsideProviderTests : IDisposable
{
    private const string DefaultUsage =
        """{"input":22744,"output":5515,"cacheRead":758400,"cacheWrite":0,"totalTokens":786659,"cost":{"total":0.65837}}""";

    private const string Schema = """
        CREATE TABLE sessions (id TEXT PRIMARY KEY, model TEXT);
        CREATE TABLE session_turns (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT REFERENCES sessions(id) ON DELETE CASCADE, token_usage TEXT, started_at INTEGER, last_message_timestamp INTEGER NOT NULL, finished_at INTEGER, aborted_at INTEGER);
        INSERT INTO sessions VALUES ('synthetic-session', '{"modelId":"synthetic-model"}');
        """;

    private static readonly DateTimeOffset Base = D("2026-03-04T10:00:00Z");

    private readonly string _root;
    private readonly string _home;

    public AsideProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cozy-aside-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    private static DateTimeOffset D(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private ProviderContext Ctx(DateTimeOffset? since = null) => new()
    {
        Now = D("2026-03-04T12:00:00Z"),
        Since = since ?? D("2026-03-01T00:00:00Z"),
        Env = _ => null,
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

    private static string Sec(DateTimeOffset d) => d.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>~/.aside/u/&lt;user&gt;/state.db 에 턴 하나</summary>
    private string Fixture(string user = "0", DateTimeOffset? started = null, DateTimeOffset? lastMessage = null,
        DateTimeOffset? finishedAt = null, string usage = DefaultUsage)
    {
        var db = Path.Combine(_home, ".aside", "u", user, "state.db");
        var start = started ?? Base;
        var last = lastMessage ?? start;
        var finished = finishedAt is { } f ? Sec(f) : "NULL";
        Exec(db, Schema
            + "INSERT INTO session_turns VALUES (1, 'synthetic-session', '" + usage + "', "
            + Sec(start) + ", " + Sec(last) + ", " + finished + ", NULL);");
        return db;
    }

    private static long Total(IEnumerable<UsageEntry> entries) => entries.Sum(e => e.Total);

    [Fact]
    public async Task ReadsTurnBucketsWithoutCountingTotalAgain()
    {
        Fixture();

        var entries = await new AsideUsageProvider().LoadAsync(Ctx());

        var e = Assert.Single(entries);
        Assert.Equal(22744, e.Input);
        Assert.Equal(5515, e.Output);
        Assert.Equal(758400, e.CacheRead);
        Assert.Equal(0, e.CacheWrite);
        Assert.Equal(786659, e.Total);
        Assert.Equal("synthetic-model", e.Model);
        Assert.True(e.ExplicitCost.HasValue && Math.Abs(e.ExplicitCost.GetValueOrDefault() - 0.65837) < 1e-9);
        Assert.True(e.CostUnavailable); // 가격표 추정 금지
        Assert.Equal(Base, e.Date);
        Assert.StartsWith("aside|", e.Id);
        Assert.EndsWith(":1", e.Id);
    }

    [Fact]
    public async Task TotalOnlyUsageFallsBackWithoutInventingCache()
    {
        Fixture(usage: """{"input":null,"totalTokens":123}""");

        var e = Assert.Single(await new AsideUsageProvider().LoadAsync(Ctx()));

        Assert.Equal(123, e.Total);
        Assert.Equal(123, e.Input);
        Assert.Equal(0, e.CacheRead);
        Assert.Null(e.ExplicitCost);
    }

    [Fact]
    public async Task ReadsEveryUserDatabase()
    {
        Fixture("0");
        Fixture("1");

        var entries = await new AsideUsageProvider().LoadAsync(Ctx());

        Assert.Equal(2, entries.Count);
        Assert.Equal(1573318, Total(entries));
    }

    [Fact]
    public async Task DeletedSessionStaysCountedUntilInvalidation()
    {
        var db = Fixture("0");
        Fixture("1");
        var provider = new AsideUsageProvider();
        Assert.Equal(1573318, Total(await provider.LoadAsync(Ctx())));

        Exec(db, "PRAGMA foreign_keys = ON; DELETE FROM sessions WHERE id = 'synthetic-session';");
        Assert.Single(await new AsideUsageProvider().LoadAsync(Ctx())); // 새 인스턴스: cascade 로 턴이 지워졌는지 확인

        Assert.Equal(1573318, Total(await provider.LoadAsync(Ctx()))); // 이미 센 사용량은 줄지 않는다
        provider.Invalidate();
        Assert.Equal(786659, Total(await provider.LoadAsync(Ctx())));
    }

    [Fact]
    public async Task GrowingTurnReplacesTheCachedValue()
    {
        var db = Fixture();
        var provider = new AsideUsageProvider();
        Assert.Equal(786659, Total(await provider.LoadAsync(Ctx())));

        Exec(db, """UPDATE session_turns SET token_usage = '{"input":22744,"output":9999,"cacheRead":758400}' WHERE id = 1;""");

        Assert.Equal(786659 + 9999 - 5515, Total(await provider.LoadAsync(Ctx())));
    }

    [Fact]
    public async Task RecreatedDatabaseGetsFreshEntryIds()
    {
        var db = Fixture();
        var provider = new AsideUsageProvider();
        Assert.Equal(786659, Total(await provider.LoadAsync(Ctx())));

        File.Delete(db);
        Fixture(usage: """{"input":10,"output":20}"""); // 새 파일, id 1 이 다시, 훨씬 작음

        Assert.Equal(786659 + 30, Total(await provider.LoadAsync(Ctx()))); // 오래된 id 뒤에 숨지 않아야 함
        provider.Invalidate();
        Assert.Equal(30, Total(await provider.LoadAsync(Ctx())));
    }

    [Fact]
    public async Task UnreadableDatabaseKeepsPreviousValuesWithoutThrowing()
    {
        var db = Fixture();
        var provider = new AsideUsageProvider();
        Assert.Equal(786659, Total(await provider.LoadAsync(Ctx())));

        File.WriteAllText(db, "not a database");

        Assert.Equal(786659, Total(await provider.LoadAsync(Ctx())));
    }

    [Fact]
    public async Task SkipsUnreadableDatabaseAndKeepsHealthyOnes()
    {
        Exec(Path.Combine(_home, ".aside", "u", "0", "state.db"), "CREATE TABLE sessions (id TEXT PRIMARY KEY, model TEXT);");
        Fixture("1");
        var garbageDir = Path.Combine(_home, ".aside", "u", "2");
        Directory.CreateDirectory(garbageDir);
        File.WriteAllText(Path.Combine(garbageDir, "state.db"), "not a database");
        Directory.CreateDirectory(Path.Combine(_home, ".aside", "u", "3", "state.db")); // state.db 라는 폴더

        var entries = await new AsideUsageProvider().LoadAsync(Ctx());

        Assert.Equal(786659, Total(entries));
    }

    [Fact]
    public async Task DropsZeroTokenTurns()
    {
        Fixture(usage: """{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"totalTokens":0}""");

        Assert.Empty(await new AsideUsageProvider().LoadAsync(Ctx()));
    }

    [Fact]
    public async Task TurnIsDatedAtLastActivity()
    {
        var dayStart = D("2026-03-04T00:00:00Z");
        Fixture("0", started: dayStart.AddMinutes(-10), lastMessage: dayStart.AddMinutes(10));
        Fixture("1", started: dayStart.AddMinutes(-10), lastMessage: dayStart.AddMinutes(-9));

        var entries = await new AsideUsageProvider().LoadAsync(Ctx(dayStart));

        var e = Assert.Single(entries);
        Assert.Equal(dayStart.AddMinutes(10), e.Date);
    }

    [Fact]
    public async Task FinishedAtOutranksLastMessageTimestamp()
    {
        var dayStart = D("2026-03-04T00:00:00Z");
        var yesterday = dayStart.AddHours(-1);
        var today = dayStart.AddHours(1);
        Fixture("0", started: yesterday, lastMessage: yesterday, finishedAt: today);   // 오늘 끝남 → 포함
        Fixture("1", started: yesterday, lastMessage: today, finishedAt: yesterday);   // 어제 끝남 → 제외

        var entries = await new AsideUsageProvider().LoadAsync(Ctx(dayStart));

        var e = Assert.Single(entries);
        Assert.Equal(today, e.Date);
        Assert.Contains(Path.Combine("u", "0", "state.db"), e.Id);
    }

    [Fact]
    public void DatabasesIncludeRootAndDirectChildren()
    {
        var u = Path.Combine(_home, ".aside", "u");
        Exec(Path.Combine(u, "state.db"), "CREATE TABLE x (a INTEGER);");
        Exec(Path.Combine(u, "b", "state.db"), "CREATE TABLE x (a INTEGER);");
        Exec(Path.Combine(u, "a", "state.db"), "CREATE TABLE x (a INTEGER);");
        Exec(Path.Combine(u, "a", "deeper", "state.db"), "CREATE TABLE x (a INTEGER);"); // 손자는 제외

        var dbs = AsideUsageProvider.Databases(new[] { u });

        Assert.Equal(new[]
        {
            Path.GetFullPath(Path.Combine(u, "a", "state.db")),
            Path.GetFullPath(Path.Combine(u, "b", "state.db")),
            Path.GetFullPath(Path.Combine(u, "state.db")),
        }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), dbs.ToArray());
    }

    [Fact]
    public void FileIdentityChangesWhenTheFileIsRecreated()
    {
        var path = Path.Combine(_root, "identity.bin");
        File.WriteAllText(path, "one");
        var first = AsideUsageProvider.FileIdentity(path);
        Assert.Equal(first, AsideUsageProvider.FileIdentity(path)); // 같은 파일이면 안정적

        File.Delete(path);
        Thread.Sleep(20);
        File.WriteAllText(path, "two");
        Assert.NotEqual(first, AsideUsageProvider.FileIdentity(path));
    }

    [Fact]
    public void DefaultRootIsAsideUserFolder()
    {
        var roots = new AsideUsageProvider().ResolveRoots(Ctx());
        Assert.Equal(new[] { Path.GetFullPath(Path.Combine(_home, ".aside", "u")) }, roots.ToArray());
    }
}
