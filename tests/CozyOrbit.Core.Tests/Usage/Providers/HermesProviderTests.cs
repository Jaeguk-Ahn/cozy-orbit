using System.Globalization;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>원본 LocalAdditionalUsageTests 의 Hermes 픽스처 (spec §4.6).</summary>
public sealed class HermesProviderTests : IDisposable
{
    private const string Schema = """
        CREATE TABLE sessions (id TEXT PRIMARY KEY, model TEXT, billing_provider TEXT, started_at REAL NOT NULL,
          message_count INTEGER DEFAULT 0, input_tokens INTEGER DEFAULT 0, output_tokens INTEGER DEFAULT 0,
          cache_read_tokens INTEGER DEFAULT 0, cache_write_tokens INTEGER DEFAULT 0, reasoning_tokens INTEGER DEFAULT 0,
          estimated_cost_usd REAL, actual_cost_usd REAL);
        """;

    private readonly string _root;
    private readonly string _home;

    public HermesProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cozy-hermes-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    private string DefaultDb => Path.Combine(_home, ".hermes", "state.db");

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
    public async Task ReadsSessionTokensReasoningAndActualCost()
    {
        Exec(DefaultDb, Schema + """
            INSERT INTO sessions VALUES ('session-1', 'claude-sonnet-4-20250514', 'anthropic', 1767312000, 42, 100, 50, 10, 20, 5, 0.12, 0.34);
            """);

        var entries = await new HermesUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal("hermes|session-1", e.Id);
        Assert.Equal("claude-sonnet-4-20250514", e.Model);
        Assert.Equal(100, e.Input);
        Assert.Equal(55, e.Output); // output + reasoning
        Assert.Equal(20, e.CacheWrite);
        Assert.Equal(10, e.CacheRead);
        Assert.Equal(185, e.Total);
        Near(0.34, e.ExplicitCost);
        Assert.False(e.CostIsEstimate);
        Assert.True(e.CostUnavailable); // 세션 합계는 가격표로 추정하지 않음
        Assert.Equal(D("2026-01-02T00:00:00Z"), e.Date);
    }

    [Fact]
    public async Task PreservesActualZeroAndDistinguishesMissingCost()
    {
        Exec(DefaultDb, Schema + """
            INSERT INTO sessions VALUES ('zero', 'gpt-5.5', 'openai', 1767312000, 1, 1000000, 100, 0, 0, 0, 8, 0);
            INSERT INTO sessions VALUES ('estimate', 'gpt-5.5', 'openai', 1767312000, 1, 1000000, 100, 0, 0, 0, 8, NULL);
            INSERT INTO sessions VALUES ('missing', 'gpt-5.5', 'openai', 1767312000, 1, 1000000, 100, 0, 0, 0, NULL, NULL);
            """);

        var entries = await new HermesUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal(3, entries.Count);
        var zero = entries.Single(e => e.Id == "hermes|zero");
        Near(0, zero.ExplicitCost);
        Assert.False(zero.CostIsEstimate);

        var estimate = entries.Single(e => e.Id == "hermes|estimate");
        Near(8, estimate.ExplicitCost);
        Assert.True(estimate.CostIsEstimate);

        var missing = entries.Single(e => e.Id == "hermes|missing");
        Assert.Null(missing.ExplicitCost);
        Assert.True(missing.CostUnavailable);
    }

    [Fact]
    public async Task AcceptsMillisecondStartedAt()
    {
        Exec(DefaultDb, Schema + """
            INSERT INTO sessions VALUES ('session-ms', 'gpt-5', 'openai', 1767312000000, 1, 10, 5, 0, 0, 0, 0, 0);
            """);

        var entries = await new HermesUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal(15, e.Total);
        Assert.Equal(D("2026-01-02T00:00:00Z"), e.Date);
        Assert.Equal(UsageTime.LocalDay(D("2026-01-02T00:00:00Z")), e.LocalDay);
    }

    [Fact]
    public async Task HugeSessionTokensAreClampedNotOverflowed()
    {
        Exec(DefaultDb, Schema + """
            INSERT INTO sessions VALUES ('huge', 'gpt-5', 'openai', 1767312000, 1,
              9223372036854775807, 9223372036854775807, 0, 0, 9223372036854775807, NULL, NULL);
            """);

        var entries = await new HermesUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        var e = Assert.Single(entries);
        Assert.Equal(JsonValues.MaxTokenValue, e.Input);
        Assert.Equal(JsonValues.MaxTokenValue * 2, e.Output); // 컬럼별 상한 후 reasoning 을 더함
    }

    [Fact]
    public async Task SkipsBlankModelsAndOldSessions()
    {
        Exec(DefaultDb, Schema + """
            INSERT INTO sessions VALUES ('blank', '  ', 'openai', 1767312000, 1, 10, 5, 0, 0, 0, NULL, NULL);
            INSERT INTO sessions VALUES ('nomodel', NULL, 'openai', 1767312000, 1, 10, 5, 0, 0, 0, NULL, NULL);
            INSERT INTO sessions VALUES ('old', 'gpt-5', 'openai', 1767000000, 1, 10, 5, 0, 0, 0, NULL, NULL);
            INSERT INTO sessions VALUES ('empty', 'gpt-5', 'openai', 1767312000, 1, 0, 0, 0, 0, 0, NULL, NULL);
            INSERT INTO sessions VALUES ('ok', 'gpt-5', 'openai', 1767312000, 1, 10, 5, 0, 0, 0, NULL, NULL);
            """);

        var entries = await new HermesUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Equal("hermes|ok", Assert.Single(entries).Id);
    }

    [Fact]
    public async Task HermesHomeMayNameDatabasesDirectlyAndFirstIdWins()
    {
        var a = Path.Combine(_root, "a", "state.db");
        var b = Path.Combine(_root, "b", "custom.db");
        Exec(a, Schema + "INSERT INTO sessions VALUES ('s', 'gpt-5', 'openai', 1767312000, 1, 10, 0, 0, 0, 0, NULL, NULL);");
        Exec(b, Schema + "INSERT INTO sessions VALUES ('s', 'gpt-5', 'openai', 1767312000, 1, 99, 0, 0, 0, 0, NULL, NULL);");
        var env = $"{Path.GetDirectoryName(a)}, {b}";
        var ctx = Ctx(D("2026-01-01T00:00:00Z"), name => name == "HERMES_HOME" ? env : null);

        var entries = await new HermesUsageProvider().LoadAsync(ctx);

        Assert.Equal(10, Assert.Single(entries).Input); // 루트 간 같은 id 는 처음 것
    }

    [Fact]
    public async Task UnreadableDatabaseIsSkippedWithoutThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultDb)!);
        File.WriteAllText(DefaultDb, "not a sqlite database");

        var entries = await new HermesUsageProvider().LoadAsync(Ctx(D("2026-01-01T00:00:00Z")));

        Assert.Empty(entries);
    }

    [Fact]
    public void DefaultRootIsHermesHome()
    {
        var roots = new HermesUsageProvider().ResolveRoots(Ctx(D("2026-01-01T00:00:00Z")));
        Assert.Equal(new[] { Path.GetFullPath(Path.Combine(_home, ".hermes")) }, roots.ToArray());
    }
}
