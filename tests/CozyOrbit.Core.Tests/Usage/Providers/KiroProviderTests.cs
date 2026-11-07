using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>
/// Kiro CLI (원본 KiroUsageTests / KiroContentBlockTests 재사용). 토큰 = UTF-8 바이트 / 4.
/// 레이아웃: SQLite &lt;LocalAppData&gt;/kiro-cli/data.sqlite3, JSONL &lt;home&gt;/.kiro/sessions/{cli/&lt;id&gt;.jsonl, &lt;ws&gt;/&lt;sid&gt;/messages.jsonl}
/// </summary>
public sealed class KiroProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _home;
    private readonly string _local;
    private readonly string _roaming;
    private readonly string _kiroSessions;

    public KiroProviderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "cozy-kiro-" + Guid.NewGuid().ToString("N"));
        _local = Path.Combine(_home, "Local");
        _roaming = Path.Combine(_home, "Roaming");
        _kiroSessions = Path.Combine(_home, ".kiro", "sessions");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_home, recursive: true); }
        catch (Exception) { /* 무시 */ }
    }

    private string DbPath => Path.Combine(_local, "kiro-cli", "data.sqlite3");

    private ProviderContext Ctx(DateTimeOffset? now = null, DateTimeOffset? since = null, Func<string, string?>? env = null) => new()
    {
        Now = now ?? Now,
        Since = since ?? Since,
        Env = env ?? (_ => null),
        Home = _home,
        RoamingAppData = _roaming,
        LocalAppData = _local,
    };

    private static Task<IReadOnlyList<UsageEntry>> Load(KiroUsageProvider provider, ProviderContext ctx) => provider.LoadAsync(ctx);

    private Task<IReadOnlyList<UsageEntry>> Load(DateTimeOffset? since = null) => new KiroUsageProvider().LoadAsync(Ctx(since: since));

    // ---------------------------------------------------------------- SQLite fixtures

    private static string Repeat(char c, int n) => new(c, n);

    private static string Turn(long? timestampMs, string? model, string userText, string assistantText = "", long responseBytes = 0)
    {
        var meta = new List<string>();
        if (timestampMs is { } ts) meta.Add(FormattableString.Invariant($"\"request_start_timestamp_ms\":{ts}"));
        meta.Add(FormattableString.Invariant($"\"response_size\":{responseBytes}"));
        if (timestampMs != null) meta.Add("\"time_between_chunks\":[],\"tool_use_ids_and_names\":[]");
        if (model != null) meta.Add("\"model_id\":\"" + model + "\"");
        return "{\"user\":{\"content\":\"" + userText + "\"},\"assistant\":{\"content\":\"" + assistantText +
               "\"},\"request_metadata\":{" + string.Join(",", meta) + "}}";
    }

    private static string Conversation(string id, params string[] turns) =>
        "{\"conversation_id\":\"" + id + "\",\"latest_summary\":null,\"history\":[" + string.Join(",", turns) + "]}";

    private void Exec(string sql, params (string Name, object Value)[] parameters)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var cs = new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private void SeedV2(string id, string value)
    {
        Exec("CREATE TABLE IF NOT EXISTS conversations_v2 (conversation_id TEXT PRIMARY KEY, key TEXT, created_at INTEGER, updated_at INTEGER, value TEXT)");
        Exec("INSERT INTO conversations_v2 (conversation_id, key, created_at, updated_at, value) VALUES ($id, $key, 0, 0, $value)",
            ("$id", id), ("$key", "/Users/dev/project"), ("$value", value));
    }

    private void SeedV1(string cwd, string value)
    {
        Exec("CREATE TABLE IF NOT EXISTS conversations (key TEXT PRIMARY KEY, value TEXT)");
        Exec("INSERT INTO conversations (key, value) VALUES ($key, $value)", ("$key", cwd), ("$value", value));
    }

    // ---------------------------------------------------------------- JSONL fixtures

    private void SeedCli(string id, IEnumerable<(string Prompt, string Assistant, string TimestampSeconds)> turns,
        string model = "claude-sonnet-4-5", string? root = null)
    {
        var cli = Path.Combine(root ?? _kiroSessions, "cli");
        Directory.CreateDirectory(cli);
        var lines = new List<string>();
        int index = 0;
        foreach (var (prompt, assistant, ts) in turns)
        {
            index++;
            lines.Add("{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"message_id\":\"prompt-" + index +
                      "\",\"content\":[{\"kind\":\"text\",\"data\":\"" + prompt + "\"}],\"meta\":{\"timestamp\":" + ts + "}}}");
            lines.Add("{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"message_id\":\"assistant-" + index +
                      "\",\"content\":[{\"kind\":\"text\",\"data\":\"" + assistant + "\"}]}}");
        }
        File.WriteAllText(Path.Combine(cli, id + ".jsonl"), string.Join("\n", lines) + "\n");
        File.WriteAllText(Path.Combine(cli, id + ".json"),
            "{\"session_id\":\"" + id + "\",\"cwd\":\"/tmp/project\",\"created_at\":\"2026-02-13T12:00:00Z\",\"updated_at\":\"2026-02-13T12:00:01Z\",\"session_state\":{\"rts_model_state\":{\"model_info\":{\"model_id\":\"" +
            model + "\"}},\"conversation_metadata\":{\"user_turn_metadatas\":[]}}}");
    }

    private void SeedCliRaw(string name, params string[] lines)
    {
        var cli = Path.Combine(_kiroSessions, "cli");
        Directory.CreateDirectory(cli);
        File.WriteAllText(Path.Combine(cli, name + ".jsonl"), string.Join("\n", lines) + "\n");
    }

    private string SeedV3(string workspace, string sessionId, string sessionJson, params string[] lines)
    {
        var dir = Path.Combine(_kiroSessions, workspace, sessionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "session.json"), sessionJson);
        var path = Path.Combine(dir, "messages.jsonl");
        File.WriteAllText(path, string.Join("\n", lines));
        return path;
    }

    // ---------------------------------------------------------------- identity / roots

    [Fact]
    public void Identity()
    {
        IUsageProvider p = new KiroUsageProvider();
        Assert.Equal("kiro", p.Id);
        Assert.Equal("Kiro", p.DisplayName);
        Assert.False(p.IncludeModels);
    }

    [Fact]
    public void DefaultRootsAndEnvOverrides()
    {
        var roots = new KiroUsageProvider().ResolveRoots(Ctx());
        Assert.Equal(new[]
        {
            Path.GetFullPath(Path.Combine(_local, "kiro-cli")),
            Path.GetFullPath(Path.Combine(_roaming, "kiro-cli")),
            Path.GetFullPath(_kiroSessions),
        }, roots);

        var a = Path.Combine(_home, "a");
        var b = Path.Combine(_home, "b.sqlite3");
        var kiroHome = Path.Combine(_home, "kiro-home");
        Func<string, string?> env = name => name switch
        {
            "KIRO_CLI_HOME" => a + " , " + b,   // 쉼표 목록, 기본값 대체
            "KIRO_HOME" => kiroHome,
            _ => null,
        };
        Assert.Equal(new[]
        {
            Path.GetFullPath(a), Path.GetFullPath(b), Path.GetFullPath(Path.Combine(kiroHome, "sessions")),
        }, new KiroUsageProvider().ResolveRoots(Ctx(env: env)));
    }

    [Fact]
    public async Task MissingStoresAreSilent()
    {
        Assert.Empty(await Load());
    }

    // ---------------------------------------------------------------- (a) SQLite

    [Fact]
    public async Task FirstTurnInputIsTheUserMessageByteEstimate()
    {
        SeedV2("conv-1", Conversation("conv-1", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200)));
        var e = Assert.Single(await Load());
        Assert.Equal(100, e.Input);   // 400 / 4
        Assert.Equal(50, e.Output);   // 200 / 4
        Assert.Equal(0, e.CacheRead);
        Assert.Equal(0, e.CacheWrite);
        Assert.Equal("claude-sonnet-4.5", e.Model);
        Assert.Equal("kiro|conv-1|1780000000000", e.Id);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_780_000_000_000), e.Date);
        Assert.True(e.CostUnavailable);
        Assert.Null(e.ExplicitCost);
    }

    [Fact]
    public async Task LaterTurnInputIncludesAccumulatedHistory()
    {
        SeedV2("conv-1", Conversation("conv-1",
            Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), Repeat('a', 800), 800),
            Turn(1_780_000_100_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40)));
        var second = (await Load()).Single(e => e.Id == "kiro|conv-1|1780000100000");
        Assert.Equal((400 + 800 + 40) / 4, second.Input);
    }

    [Fact]
    public async Task SkippedTurnsStillContributeToHistory()
    {
        SeedV2("conv-1", Conversation("conv-1",
            Turn(null, "claude-sonnet-4.5", Repeat('u', 400), Repeat('a', 400)),
            Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40)));
        var e = Assert.Single(await Load()); // 타임스탬프 없는 턴은 건너뜀
        Assert.Equal((400 + 400 + 40) / 4, e.Input);
    }

    [Fact]
    public async Task LatestSummarySeedsTheHistory()
    {
        var json = "{\"conversation_id\":\"conv-1\",\"latest_summary\":[\"" + Repeat('s', 120) + "\"],\"history\":[" +
                   Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40) + "]}";
        SeedV2("conv-1", json);
        var e = Assert.Single(await Load());
        Assert.Equal((120 + 40) / 4, e.Input);
    }

    [Fact]
    public async Task ImagesFieldIsExcluded()
    {
        var turn = "{\"user\":{\"content\":\"" + Repeat('u', 40) + "\",\"images\":[\"" + Repeat('x', 100_000) +
                   "\"]},\"assistant\":{},\"request_metadata\":{\"request_start_timestamp_ms\":1780000000000,\"model_id\":\"claude-sonnet-4.5\",\"response_size\":40}}";
        SeedV2("conv-1", Conversation("conv-1", turn));
        Assert.Equal(10, Assert.Single(await Load()).Input);
    }

    [Fact]
    public async Task ZeroByteTurnsAndMissingModel()
    {
        SeedV2("conv-1", Conversation("conv-1",
            Turn(1_780_000_000_000, "claude-sonnet-4.5", "", responseBytes: 0),
            Turn(1_780_000_001_000, null, Repeat('u', 4), responseBytes: 0)));
        var e = Assert.Single(await Load());
        Assert.Equal("kiro|conv-1|1780000001000", e.Id);
        Assert.Equal("unknown", e.Model);
    }

    [Fact]
    public async Task TurnsBeforeTheWindowAreExcluded()
    {
        SeedV2("conv-1", Conversation("conv-1",
            Turn(1_766_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200),
            Turn(1_767_500_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200)));
        var entries = await Load(since: DateTimeOffset.FromUnixTimeMilliseconds(1_767_000_000_000));
        Assert.Equal(new[] { "kiro|conv-1|1767500000000" }, entries.Select(e => e.Id));
    }

    [Fact]
    public async Task ReadsV1TableAndSkipsRowsWithoutConversationId()
    {
        SeedV1("/Users/dev/project", Conversation("conv-2", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 800), responseBytes: 400)));
        SeedV1("/Users/dev/other", "{\"history\":[{\"request_metadata\":{\"request_start_timestamp_ms\":1780000000000,\"model_id\":\"claude-sonnet-4.5\",\"response_size\":200}}]}");
        var e = Assert.Single(await Load());
        Assert.Equal("kiro|conv-2|1780000000000", e.Id);
        Assert.Equal(200, e.Input);
        Assert.Equal(100, e.Output);
    }

    [Fact]
    public async Task BothTablesCountedButSameConversationCollapses()
    {
        var conv1 = Conversation("conv-1", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200));
        SeedV2("conv-1", conv1);
        SeedV1("/Users/dev/project", conv1); // 업그레이드 중 두 테이블에 같은 대화
        SeedV1("/Users/dev/other-project", Conversation("conv-2", Turn(1_780_000_100_000, "claude-sonnet-4.5", Repeat('u', 800), responseBytes: 400)));
        var ids = (await Load()).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[] { "kiro|conv-1|1780000000000", "kiro|conv-2|1780000100000" }, ids);
    }

    [Fact]
    public async Task MalformedRowsAndMissingHistoryAreSkipped()
    {
        SeedV2("conv-1", "{not valid json");
        SeedV2("conv-2", "{\"conversation_id\":\"conv-2\"}");
        SeedV2("conv-3", "{\"conversation_id\":\"conv-3\",\"history\":[{\"user\":{},\"assistant\":{}}]}");
        SeedV1("/Users/dev/project", "{not valid json");
        Assert.Empty(await Load());
    }

    [Fact]
    public async Task FailedOpenIsRetriedNextLoad()
    {
        var provider = new KiroUsageProvider();
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        File.WriteAllText(DbPath, "not-a-sqlite-database");
        Assert.Empty(await Load(provider, Ctx()));

        File.Delete(DbPath);
        SeedV2("conv-1", Conversation("conv-1", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40)));
        Assert.Equal(new[] { "kiro|conv-1|1780000000000" }, (await Load(provider, Ctx())).Select(e => e.Id));
    }

    [Fact]
    public async Task DeletedRowsStayCountedUntilMonthChanges()
    {
        var provider = new KiroUsageProvider();
        SeedV2("conv-1", Conversation("conv-1",
            Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200),
            Turn(1_780_000_100_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40)));
        Assert.Equal(2, (await Load(provider, Ctx())).Count);

        // /clear: conv-1 이 DB 에서 삭제되고 새 대화 conv-2 가 생김
        Exec("DELETE FROM conversations_v2");
        SeedV2("conv-2", Conversation("conv-2", Turn(1_780_000_200_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40)));
        File.SetLastWriteTimeUtc(DbPath, new DateTime(2026, 6, 25, 11, 0, 0, DateTimeKind.Utc)); // 서명 변경 보장

        var merged = (await Load(provider, Ctx())).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[] { "kiro|conv-1|1780000000000", "kiro|conv-1|1780000100000", "kiro|conv-2|1780000200000" }, merged);

        // 월이 바뀌면 기억을 버리고 현재 DB 내용만
        var nextMonth = await Load(provider, Ctx(now: new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new[] { "kiro|conv-2|1780000200000" }, nextMonth.Select(e => e.Id));
    }

    [Fact]
    public async Task UnchangedDatabaseKeepsEntriesAcrossLoads()
    {
        var provider = new KiroUsageProvider();
        SeedV2("conv-1", Conversation("conv-1", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 40), responseBytes: 40)));
        var first = await Load(provider, Ctx());
        var second = await Load(provider, Ctx()); // 서명이 같아 건너뛰어도 이전 엔트리는 유지
        Assert.Equal(first.Select(e => e.Id), second.Select(e => e.Id));
        Assert.Single(second);
    }

    [Fact]
    public async Task DirectDatabasePathAsCustomRootViaEnv()
    {
        SeedV2("conv-1", Conversation("conv-1", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200)));
        var ctx = Ctx(env: name => name == "KIRO_CLI_HOME" ? DbPath : null);
        Assert.Single(await new KiroUsageProvider().LoadAsync(ctx));
    }

    [Fact]
    public async Task RoamingAppDataHomeIsAlsoScanned()
    {
        var roamingDb = Path.Combine(_roaming, "kiro-cli", "data.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(roamingDb)!);
        var cs = new SqliteConnectionStringBuilder { DataSource = roamingDb, Pooling = false }.ToString();
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE conversations (key TEXT PRIMARY KEY, value TEXT); INSERT INTO conversations VALUES ('k', $v);";
            cmd.Parameters.AddWithValue("$v", Conversation("conv-r", Turn(1_780_000_000_000, "m", Repeat('u', 40), responseBytes: 40)));
            cmd.ExecuteNonQuery();
        }
        Assert.Equal("kiro|conv-r|1780000000000", Assert.Single(await Load()).Id);
    }

    // ---------------------------------------------------------------- (b) CLI 2.20 JSONL

    [Fact]
    public async Task CliJsonlSampleIsRead()
    {
        SeedCli("session-1", new[] { ("hello world", "response text", "1770983426.420942") });
        var e = Assert.Single(await Load());
        Assert.Equal("claude-sonnet-4-5", e.Model);
        Assert.Equal(11 / 4, e.Input);
        Assert.Equal(13 / 4, e.Output);
        Assert.Equal("kiro|cli|session-1|1770983426420", e.Id);
        Assert.Null(e.ExplicitCost);
        Assert.True(e.CostUnavailable);
    }

    [Fact]
    public async Task CliLaterTurnAccumulatesHistory()
    {
        SeedCli("session-2", new[]
        {
            (Repeat('u', 400), Repeat('a', 800), "1770983426.0"),
            (Repeat('u', 40), Repeat('a', 40), "1770983526.0"),
        });
        var second = (await Load()).Single(e => e.Id.EndsWith("|1770983526000", StringComparison.Ordinal));
        Assert.Equal((400 + 800 + 40) / 4, second.Input);
        Assert.Equal(40 / 4, second.Output);
    }

    [Fact]
    public async Task CliWithoutCompanionUsesFileStemAndUnknownModel()
    {
        SeedCliRaw("session-3",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"message_id\":\"prompt-3\",\"content\":[{\"kind\":\"text\",\"data\":\"hello world\"}],\"meta\":{\"timestamp\":1770983426.420942}}}",
            "not valid json at all",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"message_id\":\"assistant-3\",\"content\":[{\"kind\":\"text\",\"data\":\"response text\"}]}}");
        var e = Assert.Single(await Load());
        Assert.Equal("kiro|cli|session-3|1770983426420", e.Id);
        Assert.Equal("unknown", e.Model);
        Assert.True(e.Input + e.Output > 0);
    }

    [Fact]
    public async Task NestedToolResultsCountTextAndJsonOnce()
    {
        // json 블록: "가나ab"(8) + "more"(4) + 12(2) + null(0) = 14. results 사본은 세지 않는다.
        SeedCliRaw("synthetic",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}],\"meta\":{\"timestamp\":1770983426.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"done\"}]}}",
            "{\"version\":\"v1\",\"kind\":\"ToolResults\",\"data\":{\"content\":[{\"kind\":\"toolResult\",\"data\":{\"toolUseId\":\"tool-1\",\"status\":\"success\",\"content\":[{\"kind\":\"text\",\"data\":\"" +
            Repeat('r', 800) + "\"},{\"kind\":\"json\",\"data\":{\"stdout\":\"가나ab\",\"nested\":[\"more\",12,null]}}]}}],\"results\":{\"tool-1\":{\"result\":{\"Success\":{\"items\":\"" +
            string.Concat(Enumerable.Repeat("duplicate", 100)) + "\"}}}}}}");
        var e = Assert.Single(await Load());
        Assert.Equal((4 + 800 + 14) / 4, e.Input);
        Assert.Equal(1, e.Output);
    }

    [Fact]
    public async Task ToolArgumentsAndThinkingCountButMetadataAndImagesDoNot()
    {
        var opaque = Repeat('x', 1_024);
        var image = "{\"kind\":\"image\",\"data\":{\"format\":\"png\",\"source\":{\"bytes\":\"" + opaque + "\"}}}";
        SeedCliRaw("synthetic",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}," + image + "],\"meta\":{\"timestamp\":1770983426.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[" +
            "{\"kind\":\"thinking\",\"data\":{\"text\":\"think think!\",\"signature\":\"" + opaque + "\",\"redactedContent\":[\"" + opaque + "\"],\"modelId\":\"model\",\"toolsDigest\":\"" + opaque + "\"}}," +
            "{\"kind\":\"toolUse\",\"data\":{\"name\":\"" + opaque + "\",\"toolUseId\":\"" + opaque + "\",\"input\":{\"path\":\"file\",\"content\":\"abcd\"}}}," +
            "{\"kind\":\"text\",\"data\":\"done\"}]}}",
            "{\"version\":\"v1\",\"kind\":\"ToolResults\",\"data\":{\"content\":[{\"kind\":\"toolResult\",\"data\":{\"content\":[" + image + "]}}]}}");
        var e = Assert.Single(await Load());
        Assert.Equal(1, e.Input);
        Assert.Equal((12 + 8 + 4) / 4, e.Output);
    }

    [Fact]
    public async Task ToolContentAccumulatesAndClearResetsHistory()
    {
        SeedCliRaw("synthetic",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}],\"meta\":{\"timestamp\":1770983426.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[{\"kind\":\"thinking\",\"data\":{\"text\":\"plan\"}},{\"kind\":\"toolUse\",\"data\":{\"input\":{\"path\":\"file\",\"content\":\"abcd\"}}},{\"kind\":\"text\",\"data\":\"done\"}]}}",
            "{\"version\":\"v1\",\"kind\":\"ToolResults\",\"data\":{\"content\":[{\"kind\":\"toolResult\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcdefgh\"},{\"kind\":\"json\",\"data\":{\"stdout\":\"ijklmnop\"}}]}}]}}",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}],\"meta\":{\"timestamp\":1771069826.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"done\"}]}}",
            "{\"version\":\"v1\",\"kind\":\"Clear\",\"data\":{\"content\":[]}}",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}],\"meta\":{\"timestamp\":1771069926.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"done\"}]}}");
        var all = (await Load()).OrderBy(e => e.Date).ToList();
        Assert.Equal(new long[] { 5, 10, 1 }, all.Select(e => e.Input));
        Assert.Equal(new long[] { 4, 1, 1 }, all.Select(e => e.Output));
        Assert.Equal("kiro|cli|synthetic|1770983426000", all[0].Id);

        // 창 밖 턴의 도구 내용도 history 로 재전송된다
        var recent = (await Load(since: DateTimeOffset.FromUnixTimeSeconds(1_771_069_826))).OrderBy(e => e.Date).ToList();
        Assert.Equal(all.Skip(1).Select(e => e.Id), recent.Select(e => e.Id));
        Assert.Equal(new long[] { 10, 1 }, recent.Select(e => e.Input));
    }

    [Fact]
    public async Task MissingAndUnknownBlockPayloadsDoNotInventContent()
    {
        SeedCliRaw("synthetic",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}],\"meta\":{\"timestamp\":1770983426.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[{\"kind\":\"toolUse\",\"data\":{}},{\"kind\":\"thinking\",\"data\":{\"signature\":\"opaque\"}},{\"kind\":\"thinking\",\"data\":null},{\"kind\":\"unsupported\",\"data\":{\"text\":\"do not count\"}},{\"kind\":\"text\",\"data\":\"done\"}]}}",
            "{\"version\":\"v1\",\"kind\":\"ToolResults\",\"data\":{\"content\":[{\"kind\":\"toolResult\",\"data\":{}},{\"kind\":\"toolResult\",\"data\":{\"content\":[{\"kind\":\"json\"},{\"kind\":\"json\",\"data\":null}]}}]}}");
        var e = Assert.Single(await Load());
        Assert.Equal(1, e.Input);
        Assert.Equal(1, e.Output);
    }

    [Fact]
    public async Task LateToolResultUpdatesSameEntryWithoutDoubleCounting()
    {
        var provider = new KiroUsageProvider();
        SeedCliRaw("synthetic",
            "{\"version\":\"v1\",\"kind\":\"Prompt\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcd\"}],\"meta\":{\"timestamp\":1770983426.0}}}",
            "{\"version\":\"v1\",\"kind\":\"AssistantMessage\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"done\"}]}}");
        var first = Assert.Single(await Load(provider, Ctx()));
        Assert.Equal(1, first.Input);

        File.AppendAllText(Path.Combine(_kiroSessions, "cli", "synthetic.jsonl"),
            "{\"version\":\"v1\",\"kind\":\"ToolResults\",\"data\":{\"content\":[{\"kind\":\"toolResult\",\"data\":{\"content\":[{\"kind\":\"text\",\"data\":\"abcdefghijklmnop\"}]}}]}}\n");
        var updated = Assert.Single(await Load(provider, Ctx()));
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(5, updated.Input); // keep-max 로 갱신, 중복 없음
        Assert.Equal(1, updated.Output);
    }

    [Fact]
    public async Task UnrelatedJsonlFilesAreIgnored()
    {
        Directory.CreateDirectory(_kiroSessions);
        File.WriteAllText(Path.Combine(_kiroSessions, "notes.jsonl"), "{\"role\":\"user\",\"content\":\"ignore me please\"}\n");
        Assert.Empty(await Load());
    }

    [Fact]
    public async Task SqliteAndJsonlAreBothCounted()
    {
        SeedV2("conv-sqlite", Conversation("conv-sqlite", Turn(1_780_000_000_000, "claude-sonnet-4.5", Repeat('u', 400), responseBytes: 200)));
        SeedCli("session-jsonl", new[] { (Repeat('u', 400), Repeat('a', 200), "1770983426.420942") });
        var entries = await Load();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id.StartsWith("kiro|conv-sqlite|", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Id.StartsWith("kiro|cli|session-jsonl|", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- (c) v3 / IDE JSONL

    [Fact]
    public async Task V3StructuredMessagesAreRead()
    {
        SeedV3("my-project", "sess_abc",
            "{\"schemaVersion\":\"1.0.0\",\"id\":\"sess_abc\",\"modelId\":\"claude-sonnet-4-5\",\"createdAt\":\"2026-06-20T10:00:00Z\",\"lastModifiedAt\":\"2026-06-20T10:00:00Z\"}",
            "{\"timestamp\":\"2026-06-20T10:00:00Z\",\"payload\":{\"type\":\"user\",\"content\":\"" + Repeat('u', 400) + "\"}}",
            "{\"timestamp\":\"2026-06-20T10:00:00Z\",\"payload\":{\"type\":\"assistant\",\"content\":\"" + Repeat('a', 200) + "\"}}",
            "{\"payload\":{\"type\":\"usage_summary\",\"promptTurnSummaries\":[{\"usage\":2.5}]}}",
            "{\"payload\":{\"type\":\"turn_end\"},\"timestamp\":\"2026-06-20T10:00:00Z\"}");
        var e = Assert.Single(await Load());
        Assert.Equal("claude-sonnet-4-5", e.Model);
        Assert.Equal(100, e.Input);
        Assert.Equal(50, e.Output);
        Assert.Equal("kiro|v3|sess_abc|0", e.Id);
        Assert.Null(e.ExplicitCost); // credits 는 달러가 아니다
        Assert.Equal(new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero), e.Date);
    }

    [Fact]
    public async Task V3FlatMultiTurnWithoutTimestampsKeepsEveryTurn()
    {
        SeedV3("ws", "sess_multi",
            "{\"schemaVersion\":\"1.0.0\",\"id\":\"sess_multi\",\"createdAt\":\"2026-06-30T12:57:10.991Z\",\"lastModifiedAt\":\"2026-06-30T12:57:10.991Z\"}",
            "{\"role\":\"user\",\"content\":\"" + Repeat('u', 400) + "\"}",
            "{\"role\":\"assistant\",\"content\":\"" + Repeat('a', 200) + "\"}",
            "{\"role\":\"user\",\"content\":\"" + Repeat('u', 40) + "\"}",
            "{\"role\":\"assistant\",\"content\":\"" + Repeat('a', 40) + "\"}");
        var entries = (await Load()).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "kiro|v3|sess_multi|0", "kiro|v3|sess_multi|1" }, entries.Select(e => e.Id));
        Assert.Equal(100, entries[0].Input);
        Assert.Equal(50, entries[0].Output);
        Assert.Equal((400 + 200 + 40) / 4, entries[1].Input);
        Assert.Equal("unknown", entries[0].Model);
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 12, 57, 10, 991, TimeSpan.Zero), entries[0].Date); // createdAt 폴백
    }

    [Fact]
    public async Task V3ToolCallArgsCountTowardOutput()
    {
        SeedV3("ws", "sess_tools",
            "{\"schemaVersion\":\"1.0.0\",\"id\":\"sess_tools\",\"modelId\":\"claude-sonnet-4-5\",\"createdAt\":\"2026-06-20T10:00:00Z\"}",
            "{\"timestamp\":\"2026-06-20T10:00:00Z\",\"payload\":{\"type\":\"user\",\"content\":\"" + Repeat('u', 400) + "\"}}",
            "{\"payload\":{\"type\":\"tool_call\",\"args\":\"" + Repeat('t', 200) + "\"}}",
            "{\"payload\":{\"type\":\"turn_end\"},\"timestamp\":\"2026-06-20T10:00:05Z\"}");
        var e = Assert.Single(await Load());
        Assert.Equal(100, e.Input);
        Assert.Equal(50, e.Output);
    }

    [Fact]
    public async Task V3SessionIdFallsBackToDirectoryName()
    {
        SeedV3("ws", "dir-session", "{}",
            "{\"timestamp\":\"2026-06-20T10:00:00Z\",\"payload\":{\"type\":\"user\",\"content\":\"abcd\"}}",
            "{\"payload\":{\"type\":\"assistant\",\"content\":\"efgh\"}}");
        var e = Assert.Single(await Load());
        Assert.Equal("kiro|v3|dir-session|0", e.Id);
    }
}
