using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Usage;
using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

/// <summary>원본 AntigravityUsageTests 의 인코더 기반 픽스처 (spec §4.4).</summary>
public sealed class AntigravityProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string _home;
    private readonly string _conversations;

    public AntigravityProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cozy-antigravity-" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_root, "home");
        _conversations = Path.Combine(_home, ".gemini", "antigravity", "conversations");
        Directory.CreateDirectory(_conversations);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* 임시 폴더 정리 실패는 무시 */ }
    }

    private static DateTimeOffset D(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static ulong Secs(DateTimeOffset d) => (ulong)d.ToUnixTimeSeconds();

    private ProviderContext Ctx(DateTimeOffset? since = null) => new()
    {
        Now = D("2026-03-10T12:00:00Z"),
        Since = since ?? D("2026-01-01T00:00:00Z"),
        Env = _ => null,
        Home = _home,
        RoamingAppData = Path.Combine(_root, "roaming"),
        LocalAppData = Path.Combine(_root, "local"),
    };

    private Task<IReadOnlyList<UsageEntry>> ReadAll(DateTimeOffset? since = null) =>
        new AntigravityUsageProvider().LoadAsync(Ctx(since));

    // ---------- 테스트 쪽 protobuf 인코더 ----------

    private static class Pb
    {
        public static byte[] Raw(ulong value)
        {
            var bytes = new List<byte>();
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0)
                {
                    bytes.Add(b);
                    return bytes.ToArray();
                }
                bytes.Add((byte)(b | 0x80));
            }
        }

        public static byte[] Varint(int field, ulong value) => Cat(Raw((ulong)field << 3), Raw(value));
        public static byte[] Message(int field, byte[] payload) => Cat(Raw(((ulong)field << 3) | 2), Raw((ulong)payload.Length), payload);
        public static byte[] Str(int field, string text) => Message(field, Encoding.UTF8.GetBytes(text));
        public static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    }

    /// <summary>CortexStepGeneratorMetadata { 1 chat_model { 3, 4 usage, 9 chat_start { 4 created_at }, 19 model }, 4 execution_id }</summary>
    private static byte[] MakeRecord(string? responseId, string model, ulong? createdAtSeconds,
        ulong input, ulong output, ulong cacheRead, ulong? thinking = null, ulong? response = null, string? execution = null)
    {
        var usage = new List<byte[]>
        {
            Pb.Varint(1, 1071), Pb.Varint(2, input), Pb.Varint(3, output), Pb.Varint(5, cacheRead), Pb.Varint(6, 24),
        };
        if (thinking is { } t) usage.Add(Pb.Varint(9, t));
        if (response is { } r) usage.Add(Pb.Varint(10, r));
        if (responseId != null) usage.Add(Pb.Str(11, responseId));

        // 2.0 은 chat_start_metadata 는 쓰되 그 안의 created_at 을 뺐다
        var chatStart = createdAtSeconds is { } s ? Pb.Message(4, Pb.Varint(1, s)) : Pb.Varint(2, ulong.MaxValue);
        var chatModel = Pb.Cat(Pb.Varint(3, 1071), Pb.Message(4, Pb.Cat(usage.ToArray())), Pb.Message(9, chatStart), Pb.Str(19, model));
        var record = Pb.Message(1, chatModel);
        return execution != null ? Pb.Cat(record, Pb.Str(4, execution)) : record;
    }

    private static byte[] Gen(string? responseId, string model, DateTimeOffset createdAt,
        ulong input, ulong output, ulong cacheRead, ulong? thinking = null, ulong? response = null) =>
        MakeRecord(responseId, model, Secs(createdAt), input, output, cacheRead, thinking, response);

    private static byte[] Undated(string? responseId, string execution, ulong input, ulong output, ulong cacheRead) =>
        MakeRecord(responseId, "gemini-3.7-flash", null, input, output, cacheRead, execution: execution);

    /// <summary>StepMetadata { 1 created_at, 8 finished_at, 9 { 11 response_id }, 12 execution_id }</summary>
    private static byte[] MakeStep(string? execution, string? responseId, DateTimeOffset queued, DateTimeOffset? finished)
    {
        var parts = new List<byte[]> { Pb.Message(1, Pb.Varint(1, Secs(queued))) };
        if (finished is { } f) parts.Add(Pb.Message(8, Pb.Varint(1, Secs(f))));
        if (responseId != null) parts.Add(Pb.Message(9, Pb.Str(11, responseId)));
        if (execution != null) parts.Add(Pb.Str(12, execution));
        return Pb.Cat(parts.ToArray());
    }

    private static SqliteConnection OpenWrite(string db)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = db,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void InsertGenerations(SqliteConnection conn, IReadOnlyList<byte[]> blobs, int startIndex = 0)
    {
        for (int i = 0; i < blobs.Count; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO gen_metadata VALUES ($idx, $data, $size)";
            cmd.Parameters.AddWithValue("$idx", (long)(startIndex + i));
            cmd.Parameters.AddWithValue("$data", blobs[i]);
            cmd.Parameters.AddWithValue("$size", (long)blobs[i].Length);
            cmd.ExecuteNonQuery();
        }
    }

    private string WriteConversation(string name, IReadOnlyList<byte[]> records, IReadOnlyList<byte[]>? steps = null,
        string? directory = null, bool walMode = false)
    {
        var dir = directory ?? _conversations;
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, name + ".db");
        using var conn = OpenWrite(db);
        if (walMode) Exec(conn, "PRAGMA journal_mode=WAL;");
        Exec(conn, "CREATE TABLE gen_metadata (idx integer, data blob, size integer NOT NULL DEFAULT 0, PRIMARY KEY (idx));");
        InsertGenerations(conn, records);
        if (steps != null && steps.Count > 0)
        {
            Exec(conn, "CREATE TABLE steps (idx integer, metadata blob, PRIMARY KEY (idx));");
            for (int i = 0; i < steps.Count; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO steps VALUES ($idx, $metadata)";
                cmd.Parameters.AddWithValue("$idx", (long)i);
                cmd.Parameters.AddWithValue("$metadata", steps[i]);
                cmd.ExecuteNonQuery();
            }
        }
        return db;
    }

    private static void AssertSameInstant(DateTimeOffset expected, DateTimeOffset actual, double toleranceSeconds = 0.001) =>
        Assert.True(Math.Abs((expected - actual).TotalSeconds) <= toleranceSeconds, $"expected {expected:o}, got {actual:o}");

    // ---------- 토큰 매핑 ----------

    [Fact]
    public async Task TokenMappingKeepsTheWriterSemantics()
    {
        var created = D("2026-03-04T10:00:00Z");
        WriteConversation("c1", new[]
        {
            Gen("r1", "gemini-3.6-flash", created, input: 4667, output: 462, cacheRead: 52968, thinking: 398, response: 64),
        });

        var e = Assert.Single(await ReadAll());

        Assert.Equal("antigravity|r1", e.Id);
        Assert.Equal(4667, e.Input);        // 이미 캐시 읽기 제외
        Assert.Equal(52968, e.CacheRead);
        Assert.Equal(462, e.Output);        // thinking + response 의 합 — 다시 더하지 않음
        Assert.Equal(0, e.CacheWrite);
        Assert.Equal(4667 + 462 + 52968, e.Total);
        Assert.Equal("antigravity/gemini-3.6-flash", e.Model);
        Assert.Null(e.ExplicitCost);
        Assert.Null(ModelPricing.EstimatedCost(e.Model, e.Input, e.Output, e.CacheWrite, e.CacheRead)); // 가격 없음
        AssertSameInstant(created, e.Date);
        Assert.Equal(UsageTime.LocalDay(created), e.LocalDay);
    }

    [Fact]
    public async Task RowWithNoTokensProducesNoEntry()
    {
        WriteConversation("c1", new[] { Gen("r1", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 0, 0, 0) });
        Assert.Empty(await ReadAll());
    }

    // ---------- id ----------

    [Fact]
    public async Task ResponseIdDeduplicatesAcrossConversations()
    {
        var shared = Gen("same-call", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 100, 20, 300);
        WriteConversation("c1", new[] { shared });
        WriteConversation("c2", new[] { shared });

        var e = Assert.Single(await ReadAll());
        Assert.Equal("antigravity|same-call", e.Id);
    }

    [Fact]
    public async Task RecordWithoutResponseIdFallsBackToConversationAndIndex()
    {
        WriteConversation("c1", new[] { Gen(null, "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 100, 20, 300) });
        Assert.Equal("antigravity|c1|0", Assert.Single(await ReadAll()).Id);
    }

    // ---------- 시각 ----------

    [Fact]
    public async Task RecordsBeforeTheWindowAreExcluded()
    {
        WriteConversation("c1", new[]
        {
            Gen("old", "gemini-3.6-flash", D("2026-03-01T10:00:00Z"), 100, 20, 300),
            Gen("new", "gemini-3.6-flash", D("2026-03-09T10:00:00Z"), 100, 20, 300),
        });

        var entries = await ReadAll(D("2026-03-05T00:00:00Z"));

        Assert.Equal("antigravity|new", Assert.Single(entries).Id);
    }

    [Fact]
    public async Task ImplausibleTimestampIsRejected()
    {
        WriteConversation("c1", new[]
        {
            MakeRecord("r1", "gemini-3.6-flash", ulong.MaxValue, 100, 20, 300),
            MakeRecord("r2", "gemini-3.6-flash", 0, 100, 20, 300),
        });
        Assert.Empty(await ReadAll());
    }

    [Fact]
    public async Task StepFinishTimeDatesAnUndatedRecord()
    {
        var queued = D("2026-03-04T10:00:00Z");
        var finished = D("2026-03-04T10:00:12Z");
        var db = WriteConversation("c1", new[] { Undated("r1", "e1", 100, 20, 300) },
            new[] { MakeStep("e1", null, queued, finished) });
        // 저장소 mtime 은 턴보다 훨씬 뒤 — mtime 을 쓰면 다음 날로 밀린다
        File.SetLastWriteTimeUtc(db, D("2026-03-05T09:00:00Z").UtcDateTime);

        var e = Assert.Single(await ReadAll());
        AssertSameInstant(finished, e.Date);
        Assert.Equal(420, e.Total);
        Assert.Equal("antigravity/gemini-3.7-flash", e.Model);
    }

    [Fact]
    public async Task TheJoinIsTheExecutionIdAndNotTheRowIndex()
    {
        var first = D("2026-03-04T10:00:00Z");
        var second = D("2026-03-05T18:00:00Z");
        WriteConversation("c1", new[] { Undated("r1", "e2", 100, 20, 300) }, new[]
        {
            MakeStep("e1", null, first, first),
            MakeStep("e2", null, second, second),
        });

        AssertSameInstant(second, Assert.Single(await ReadAll()).Date);
    }

    [Fact]
    public async Task StepTimesAreHandedOutInOrderWithinAnExecution()
    {
        var times = new[] { D("2026-03-04T23:50:00Z"), D("2026-03-04T23:55:00Z"), D("2026-03-05T00:05:00Z") };
        WriteConversation("c1",
            Enumerable.Range(0, 3).Select(i => Undated("r" + i, "e1", 100, 20, 300)).ToList(),
            times.Select(t => MakeStep("e1", null, t, t)).ToList());

        var dates = (await ReadAll()).OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => e.Date).ToArray();

        Assert.Equal(times, dates);
    }

    [Fact]
    public async Task RecordsBeyondTheStepsFallBackToTheLastKnownTime()
    {
        var queued = D("2026-03-04T10:00:00Z");
        WriteConversation("c1",
            new[] { Undated("r0", "e1", 100, 20, 300), Undated("r1", "e1", 100, 20, 300) },
            new[] { MakeStep("e1", null, queued, null) }); // 실행 중: finished_at 없음

        var entries = await ReadAll();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => AssertSameInstant(queued, e.Date));
    }

    [Fact]
    public async Task ResponseIdCorrelationWinsOverTableOrder()
    {
        var firstDate = D("2026-08-18T10:00:00Z");
        var secondDate = D("2026-08-19T10:00:00Z");
        WriteConversation("c1",
            new[] { Undated("call-1", "shared", 100, 20, 300), Undated("call-2", "shared", 200, 30, 400) },
            new[]
            {
                // 일부러 역순: response_id 로 맞춰야 한다
                MakeStep("shared", "call-2", secondDate, secondDate),
                MakeStep("shared", "call-1", firstDate, firstDate),
            });

        var entries = await ReadAll();

        var first = entries.Single(e => e.Id == "antigravity|call-1");
        var second = entries.Single(e => e.Id == "antigravity|call-2");
        AssertSameInstant(firstDate, first.Date);
        AssertSameInstant(secondDate, second.Date);
        Assert.Equal(420, first.Total);
        Assert.Equal(630, second.Total);
    }

    [Fact]
    public async Task WriterCreatedAtWinsOverTheStepTime()
    {
        var created = D("2026-03-04T10:00:00Z");
        WriteConversation("c1",
            new[] { MakeRecord("r1", "gemini-3.6-flash", Secs(created), 100, 20, 300, execution: "e1") },
            new[] { MakeStep("e1", null, D("2026-03-09T10:00:00Z"), D("2026-03-09T10:00:01Z")) });

        AssertSameInstant(created, Assert.Single(await ReadAll()).Date);
    }

    [Fact]
    public async Task StoreMtimeRemainsTheLastResort()
    {
        var db = WriteConversation("c1", new[] { Undated("r1", "e1", 100, 20, 300) },
            new[] { MakeStep("another-execution", null, D("2026-03-01T10:00:00Z"), null) });
        var touched = D("2026-03-05T09:00:00Z");
        File.SetLastWriteTimeUtc(db, touched.UtcDateTime);

        var e = Assert.Single(await ReadAll());
        AssertSameInstant(touched, e.Date, toleranceSeconds: 1);
        Assert.Equal(420, e.Total);
    }

    // ---------- 비정상 입력 ----------

    [Fact]
    public async Task SentinelTokenCountIsDiscardedRatherThanTrapping()
    {
        WriteConversation("c1", new[] { Gen("r1", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), ulong.MaxValue, 20, 300) });

        var e = Assert.Single(await ReadAll());
        Assert.Equal(0, e.Input);
        Assert.Equal(320, e.Total); // 나머지는 그대로
    }

    [Fact]
    public async Task MalformedAndTruncatedBlobsAreIgnored()
    {
        var full = Gen("r1", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 100, 20, 300);
        var truncated = full.Take(full.Length - full.Length / 2).ToArray();
        WriteConversation("c1", new[] { Enumerable.Repeat((byte)0xFF, 11).ToArray(), truncated });

        Assert.Empty(await ReadAll());
    }

    [Fact]
    public async Task DatabaseWithoutTheExpectedTableIsIgnored()
    {
        using (var conn = OpenWrite(Path.Combine(_conversations, "c1.db")))
            Exec(conn, "CREATE TABLE something_else (a INTEGER);");
        File.WriteAllText(Path.Combine(_conversations, "c2.db"), "not a sqlite database");

        Assert.Empty(await ReadAll());
    }

    [Fact]
    public async Task MissingDirectoriesYieldNoEntries()
    {
        Directory.Delete(_conversations);
        Assert.Empty(await ReadAll());
    }

    [Fact]
    public async Task CheckpointedWalDatabaseIsStillReadable()
    {
        // WAL 모드 저장소가 체크포인트되어 -wal/-shm 이 없으면 mode=ro 가 실패할 수 있다 → immutable=1 폴백
        var db = WriteConversation("c1", new[] { Gen("r1", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 100, 20, 300) },
            walMode: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(db + suffix)) File.Delete(db + suffix);

        Assert.Single(await ReadAll());
    }

    // ---------- 루트 ----------

    [Fact]
    public async Task EveryDefaultRootIsScanned()
    {
        var stamp = D("2026-03-04T10:00:00Z");
        var relative = new[] { "antigravity", "antigravity-cli", "antigravity-ide" };
        for (int i = 0; i < relative.Length; i++)
        {
            var dir = Path.Combine(_home, ".gemini", relative[i], "conversations");
            WriteConversation("c" + i, new[] { Gen("r" + i, "gemini-3.6-flash", stamp, (ulong)(10 + i), 1, 0) }, directory: dir);
        }

        var ids = (await ReadAll()).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "antigravity|r0", "antigravity|r1", "antigravity|r2" }, ids);
    }

    [Fact]
    public async Task CustomRootIsAddedAndDuplicatesCollapse()
    {
        var extra = Path.Combine(_root, "extra-conversations");
        var stamp = D("2026-03-04T10:00:00Z");
        WriteConversation("c1", new[]
        {
            Gen("r1", "gemini-3.6-flash", stamp, 100, 50, 0),
            Gen("shared", "gemini-3.6-flash", stamp, 200, 100, 0),
        });
        WriteConversation("c2", new[]
        {
            Gen("r2", "gemini-3.6-flash", stamp, 300, 150, 0),
            Gen("shared", "gemini-3.6-flash", stamp, 200, 100, 0),
        }, directory: extra);
        var ctx = new ProviderContext
        {
            Now = D("2026-03-10T12:00:00Z"),
            Since = D("2026-01-01T00:00:00Z"),
            Env = _ => null,
            CustomRootsRaw = extra,
            Home = _home,
            RoamingAppData = Path.Combine(_root, "roaming"),
            LocalAppData = Path.Combine(_root, "local"),
        };

        var provider = new AntigravityUsageProvider();
        var entries = await provider.LoadAsync(ctx);

        Assert.Contains(Path.GetFullPath(extra), provider.ResolveRoots(ctx));
        Assert.Equal(
            new[] { "antigravity|r1", "antigravity|r2", "antigravity|shared" },
            entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    // ---------- 캐시 ----------

    [Fact]
    public async Task UnchangedStoreIsServedFromTheCache()
    {
        var db = WriteConversation("c1", new[] { Gen("onDisk", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 100, 20, 300) });
        var provider = new AntigravityUsageProvider();
        Assert.Equal("antigravity|onDisk", Assert.Single(await provider.LoadAsync(Ctx())).Id);

        // 같은 크기·같은 mtime 으로 내용을 망가뜨린다 → 서명이 같으므로 다시 열지 않아야 한다
        var mtime = File.GetLastWriteTimeUtc(db);
        var length = new FileInfo(db).Length;
        File.WriteAllBytes(db, Enumerable.Repeat((byte)0xFF, (int)length).ToArray());
        File.SetLastWriteTimeUtc(db, mtime);

        Assert.Equal("antigravity|onDisk", Assert.Single(await provider.LoadAsync(Ctx())).Id);
    }

    [Fact]
    public async Task UnreadableStoreKeepsThePreviousRows()
    {
        var db = WriteConversation("c1", new[] { Gen("onDisk", "gemini-3.6-flash", D("2026-03-04T10:00:00Z"), 100, 20, 300) });
        var provider = new AntigravityUsageProvider();
        Assert.Single(await provider.LoadAsync(Ctx()));

        File.WriteAllText(db, "not a sqlite database"); // 서명이 바뀌고 읽기는 실패

        Assert.Equal("antigravity|onDisk", Assert.Single(await provider.LoadAsync(Ctx())).Id);
        Assert.Empty(await new AntigravityUsageProvider().LoadAsync(Ctx())); // 캐시가 없으면 아무것도 없음
    }

    [Fact]
    public async Task ChangedStoreIsReReadWithoutWaiting()
    {
        var created = D("2026-03-04T10:00:00Z");
        var db = WriteConversation("c1", new[] { Gen("first", "gemini-3.6-flash", created, 100, 20, 300) });
        var provider = new AntigravityUsageProvider();
        Assert.Single(await provider.LoadAsync(Ctx()));

        using (var conn = OpenWrite(db))
            InsertGenerations(conn, new[] { Gen("second", "gemini-3.6-flash", created.AddSeconds(1), 100, 20, 300) }, startIndex: 1);
        File.SetLastWriteTimeUtc(db, DateTime.UtcNow.AddSeconds(5)); // 거친 mtime 해상도의 파일 시스템 대비

        var ids = (await provider.LoadAsync(Ctx())).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "antigravity|first", "antigravity|second" }, ids);
    }

    // ---------- wire 리더 ----------

    [Fact]
    public void WireReaderReturnsFirstOccurrenceOfTheRightWireType()
    {
        var data = new ReadOnlyMemory<byte>(Pb.Cat(Pb.Str(1, "text"), Pb.Varint(1, 7), Pb.Varint(1, 8), Pb.Str(2, "b")));

        Assert.True(ProtoWire.TryVarint(data, 1, out var v));
        Assert.Equal(7UL, v);
        Assert.Equal("text", ProtoWire.Text(data, 1));
        Assert.Equal("b", ProtoWire.Text(data, 2));
        Assert.False(ProtoWire.TryVarint(data, 3, out _));
    }

    [Fact]
    public void WireReaderStopsOnMalformedInput()
    {
        // 11 바이트 연속 varint → 해석 불가
        var tooLong = new ReadOnlyMemory<byte>(Enumerable.Repeat((byte)0xFF, 11).ToArray());
        Assert.False(ProtoWire.TryVarint(tooLong, 1, out _));

        // 길이가 남은 바이트보다 큼
        var overrun = new ReadOnlyMemory<byte>(new byte[] { (1 << 3) | 2, 10, 1, 2 });
        Assert.False(ProtoWire.TryMessage(overrun, 1, out _));

        // 잘못된 UTF-8 은 문자열이 아니다
        var badUtf8 = new ReadOnlyMemory<byte>(Pb.Message(1, new byte[] { 0xC3, 0x28 }));
        Assert.Null(ProtoWire.Text(badUtf8, 1));

        // 그룹(wire type 3) 을 만나면 멈춘다
        var group = new ReadOnlyMemory<byte>(Pb.Cat(new byte[] { (1 << 3) | 3 }, Pb.Varint(2, 5)));
        Assert.False(ProtoWire.TryVarint(group, 2, out _));
    }

    [Fact]
    public void WireReaderSkipsFixedWidthFields()
    {
        var fixed64 = Pb.Cat(Pb.Raw((1 << 3) | 1), new byte[8]);
        var fixed32 = Pb.Cat(Pb.Raw((2 << 3) | 5), new byte[4]);
        var data = new ReadOnlyMemory<byte>(Pb.Cat(fixed64, fixed32, Pb.Varint(3, 42)));

        Assert.True(ProtoWire.TryVarint(data, 3, out var v));
        Assert.Equal(42UL, v);
    }

    [Fact]
    public void TokenCountAndTimestampBounds()
    {
        var usage = new ReadOnlyMemory<byte>(Pb.Cat(Pb.Varint(2, 1_000_000_000), Pb.Varint(3, 1_000_000_001)));
        int discarded = 0;
        Assert.Equal(1_000_000_000L, ProtoWire.TokenCount(usage, 2, ref discarded));
        Assert.Equal(0L, ProtoWire.TokenCount(usage, 3, ref discarded));
        Assert.Equal(0L, ProtoWire.TokenCount(usage, 4, ref discarded)); // 없음 = 0, 버림 아님
        Assert.Equal(1, discarded);

        var stamp = new ReadOnlyMemory<byte>(Pb.Cat(Pb.Varint(1, 1_772_618_400), Pb.Varint(2, 500_000_000)));
        Assert.Equal(D("2026-03-04T10:00:00.5Z"), ProtoWire.StampDate(stamp));
        Assert.Null(ProtoWire.StampDate(new ReadOnlyMemory<byte>(Pb.Varint(1, 999_999_999))));
        Assert.Null(ProtoWire.StampDate(new ReadOnlyMemory<byte>(Pb.Varint(1, 4_102_444_801))));
    }
}
