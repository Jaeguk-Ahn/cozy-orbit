using System.Text;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Antigravity: %USERPROFILE%\.gemini\antigravity{,-cli,-ide}\conversations\*.db (대화당 SQLite 하나). 원본 spec §4.4.
/// gen_metadata.data (protobuf CortexStepGeneratorMetadata) 의 ModelUsageStats 를 읽는다:
///   input = 1.4.2 (캐시 읽기 제외), output = 1.4.3 (thinking+response 합 — 9/10 을 더하지 않음),
///   cacheWrite = 1.4.4, cacheRead = 1.4.5, id = response_id(1.4.11).
/// 날짜: 1.9.4 created_at → 없으면 steps 테이블(response_id / execution_id 조인) → 없으면 저장소 mtime.
/// 저장소별 결과는 SqliteSignature(.db + -wal 의 mtime·크기)를 키로 인스턴스 캐시에 보관한다 (창 필터 전 원본 행).
/// 모델명에 "antigravity/" 접두사가 붙어 가격표에 걸리지 않는다 (구독 상품 → 비용 "알 수 없음").
/// </summary>
public sealed class AntigravityUsageProvider : IUsageProvider
{
    public string Id => "antigravity";
    public string DisplayName => "Antigravity";

    /// <summary>한 스캔에서 이름을 남기는 저장소 수 (나머지는 개수만 로그)</summary>
    private const int NamedLogLimit = 5;

    private sealed record StoreBlob(DateTime MtimeUtc, long Size, List<UsageEntry> Entries);

    private enum ReadStatus { Complete, NotAConversation, Failed }

    private sealed record ConversationRead(ReadStatus Status, List<UsageEntry> Entries, int DiscardedCounters, string? Reason)
    {
        public static ConversationRead NotAConversation() => new(ReadStatus.NotAConversation, new List<UsageEntry>(), 0, null);
        public static ConversationRead Failed(string reason) => new(ReadStatus.Failed, new List<UsageEntry>(), 0, reason);
    }

    private readonly object _gate = new();
    private Dictionary<string, StoreBlob> _blobs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx) =>
        ScanRoots.Union(new[]
        {
            ctx.HomePath(".gemini", "antigravity", "conversations"),
            ctx.HomePath(".gemini", "antigravity-cli", "conversations"),
            ctx.HomePath(".gemini", "antigravity-ide", "conversations"),
        }, ctx.CustomRootsRaw);

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run<IReadOnlyList<UsageEntry>>(() => Load(ctx), ctx.CancellationToken);

    private List<UsageEntry> Load(ProviderContext ctx)
    {
        Dictionary<string, StoreBlob> known;
        lock (_gate) known = _blobs;

        // 이번 스캔에서 방문한 저장소만으로 다시 만든다 (창 밖으로 나간/사라진 저장소는 자연히 정리됨)
        var blobs = new Dictionary<string, StoreBlob>(StringComparer.OrdinalIgnoreCase);
        var lost = new List<string>();
        var discarded = new List<string>();
        foreach (var database in Databases(ResolveRoots(ctx)))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            // 읽기 전에 stat — 읽는 도중 들어온 커밋은 다음 스캔에서 서명이 달라 다시 읽힌다
            var sig = FileScan.SqliteSignature(database);
            if (sig == null) continue;
            var signature = sig.Value;
            if (new DateTimeOffset(signature.MtimeUtc, TimeSpan.Zero) < ctx.Since) continue;

            if (known.TryGetValue(database, out var cached) && cached.MtimeUtc == signature.MtimeUtc && cached.Size == signature.Size)
            {
                blobs[database] = cached;
                continue;
            }

            var conversation = Path.GetFileNameWithoutExtension(database);
            var read = ReadConversation(database, new DateTimeOffset(signature.MtimeUtc, TimeSpan.Zero));
            switch (read.Status)
            {
                case ReadStatus.Complete:
                    blobs[database] = new StoreBlob(signature.MtimeUtc, signature.Size, read.Entries);
                    if (read.DiscardedCounters > 0)
                        discarded.Add($"conversation={conversation} discarded={read.DiscardedCounters} token counter(s) over {ProtoWire.TokenCeiling}");
                    break;
                case ReadStatus.NotAConversation:
                    // 파일의 영구 속성 — 빈 결과를 캐시해 매번 다시 열지 않는다
                    blobs[database] = new StoreBlob(signature.MtimeUtc, signature.Size, new List<UsageEntry>());
                    break;
                default:
                    // 부분 결과는 버리고, 이전 행을 "이전" 서명으로 유지해 다음 스캔에서 재시도
                    if (cached != null) blobs[database] = cached;
                    lost.Add($"conversation={conversation} reason={read.Reason}");
                    break;
            }
        }

        lock (_gate) _blobs = blobs;
        LogCapped("antigravity: lost ", lost, "conversation(s) lost this scan");
        LogCapped("antigravity: ", discarded, "conversation(s) with discarded token counters");

        return Dedup.KeepMax(blobs.Values.SelectMany(b => b.Entries).Where(e => e.Date >= ctx.Since));
    }

    private static void LogCapped(string prefix, List<string> lines, string summary)
    {
        foreach (var line in lines.Take(NamedLogLimit)) AppLog.Warn(prefix + line);
        if (lines.Count > NamedLogLimit)
            AppLog.Warn($"antigravity: {lines.Count} {summary} ({lines.Count - NamedLogLimit} not named)");
    }

    /// <summary>각 루트 바로 아래의 *.db (비재귀, 이름순)</summary>
    internal static List<string> Databases(IEnumerable<string> roots)
    {
        var result = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                result.AddRange(Directory.EnumerateFiles(root)
                    .Select(p => Path.GetFileName(p))
                    .Where(n => n.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .Select(n => Path.Combine(root, n)));
            }
            catch (Exception)
            {
                // 권한 없음 등 — 이 루트는 건너뜀
            }
        }
        return result;
    }

    // ---------- 대화 저장소 하나 읽기 ----------

    private static ConversationRead ReadConversation(string database, DateTimeOffset storeDate)
    {
        var conn = SqliteUtil.OpenReadOnly(database); // mode=ro → immutable=1 순서 (SqliteUtil)
        if (conn == null) return ConversationRead.Failed("unreadable");
        using (conn)
        {
            // 1) gen_metadata 전체 (테이블 없음 = 대화 저장소 아님)
            var generations = new List<(long Index, byte[] Data)>();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT idx, data FROM gen_metadata WHERE data IS NOT NULL ORDER BY idx";
                SqliteDataReader reader;
                try
                {
                    reader = cmd.ExecuteReader();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
                {
                    return HasTable(conn, "gen_metadata")
                        ? ConversationRead.Failed("query-error: " + ex.Message)
                        : ConversationRead.NotAConversation();
                }
                using (reader)
                {
                    while (reader.Read())
                    {
                        long index = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                        var data = SqliteUtil.ColumnBlob(reader, 1);
                        if (data != null && data.Length > 0) generations.Add((index, data));
                    }
                }
            }
            catch (Exception ex)
            {
                // BUSY / 손상 페이지 등: 대화 절반을 전체로 캐시하지 않도록 통째로 버린다
                return ConversationRead.Failed("scan-incomplete: " + ex.Message);
            }

            // 2) steps 사전 패스 (선택 테이블)
            var byResponse = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            var byExecution = new Dictionary<string, List<DateTimeOffset>>(StringComparer.Ordinal);
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT metadata FROM steps WHERE metadata IS NOT NULL ORDER BY idx";
                SqliteDataReader? reader;
                try
                {
                    reader = cmd.ExecuteReader();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
                {
                    reader = null; // steps 테이블 없음 → 빈 맵
                }
                if (reader != null)
                {
                    using (reader)
                    {
                        while (reader.Read())
                        {
                            var bytes = SqliteUtil.ColumnBlob(reader, 0);
                            if (bytes == null || bytes.Length == 0) continue;
                            AddStepDate(new ReadOnlyMemory<byte>(bytes), byResponse, byExecution);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return ConversationRead.Failed("scan-incomplete(steps): " + ex.Message);
            }

            // 3) 레코드 파싱
            var conversation = Path.GetFileNameWithoutExtension(database);
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            DateTimeOffset Fallback(string? responseId, string? executionId)
            {
                if (responseId != null && byResponse.TryGetValue(responseId, out var byId)) return byId;
                if (executionId != null && byExecution.TryGetValue(executionId, out var list) && list.Count > 0)
                {
                    taken.TryGetValue(executionId, out var ordinal);
                    taken[executionId] = ordinal + 1;
                    return list[Math.Min(ordinal, list.Count - 1)];
                }
                return storeDate;
            }

            var entries = new List<UsageEntry>();
            int discardedCounters = 0;
            foreach (var (index, data) in generations)
            {
                var (entry, discarded) = ParseGeneration(data, conversation, index, Fallback);
                discardedCounters += discarded;
                if (entry != null) entries.Add(entry);
            }
            return new ConversationRead(ReadStatus.Complete, entries, discardedCounters, null);
        }
    }

    private static bool HasTable(SqliteConnection conn, string table)
    {
        try { return SqliteUtil.TableExists(conn, table); }
        catch (Exception) { return true; } // 확인 불가 → 실패로 취급(빈 결과 캐시 금지)
    }

    /// <summary>steps.metadata (StepMetadata): 8 finished_at ?? 1 created_at, 9.11 response_id, 12 execution_id</summary>
    internal static void AddStepDate(ReadOnlyMemory<byte> metadata,
        Dictionary<string, DateTimeOffset> byResponse, Dictionary<string, List<DateTimeOffset>> byExecution)
    {
        var date = ProtoWire.Timestamp(metadata, 8) ?? ProtoWire.Timestamp(metadata, 1);
        if (date is not { } d) return;
        if (ProtoWire.TryMessage(metadata, 9, out var model) && ProtoWire.Text(model, 11) is { } responseId)
            byResponse[responseId] = d;
        if (ProtoWire.Text(metadata, 12) is { } executionId)
        {
            if (!byExecution.TryGetValue(executionId, out var list))
            {
                list = new List<DateTimeOffset>();
                byExecution[executionId] = list;
            }
            list.Add(d);
        }
    }

    /// <summary>
    /// gen_metadata.data 한 행. fallbackDate(responseId, executionId) 는 created_at 이 없을 때만 호출된다.
    /// 반환: (레코드 또는 null, 상한 초과로 버린 토큰 카운터 수)
    /// </summary>
    internal static (UsageEntry? Entry, int Discarded) ParseGeneration(byte[] blob, string conversation, long index,
        Func<string?, string?, DateTimeOffset> fallbackDate)
    {
        var data = new ReadOnlyMemory<byte>(blob);
        if (!ProtoWire.TryMessage(data, 1, out var chatModel) || !ProtoWire.TryMessage(chatModel, 4, out var usage))
            return (null, 0);

        var responseId = ProtoWire.Text(usage, 11);
        var executionId = ProtoWire.Text(data, 4);

        DateTimeOffset date;
        if (!ProtoWire.TryMessage(chatModel, 9, out var chatStart) || !ProtoWire.TryMessage(chatStart, 4, out var stamp))
            date = fallbackDate(responseId, executionId);          // created_at 없음 (Antigravity 2.0)
        else if (ProtoWire.StampDate(stamp) is { } created)
            date = created;
        else
            return (null, 0);                                       // 범위 밖 시각 = 잘못 읽은 varint

        // 저장소가 아니라 호출 자체의 id — 다른 대화로 복사된 턴이 두 번 세지지 않게
        var id = responseId != null ? "antigravity|" + responseId : "antigravity|" + conversation + "|" + index;
        var model = "antigravity/" + (ProtoWire.Text(chatModel, 19) ?? "unknown");

        int discarded = 0;
        long input = ProtoWire.TokenCount(usage, 2, ref discarded);
        long output = ProtoWire.TokenCount(usage, 3, ref discarded);
        long cacheWrite = ProtoWire.TokenCount(usage, 4, ref discarded);
        long cacheRead = ProtoWire.TokenCount(usage, 5, ref discarded);
        if (input + output + cacheWrite + cacheRead <= 0) return (null, discarded);

        return (UsageEntry.Create(id, date, model, input, output, cacheWrite, cacheRead), discarded);
    }
}

/// <summary>
/// Antigravity 메타데이터용 최소 protobuf wire 리더 (spec §4.4). 외부 파일이므로 잘못된 바이트는 오류가 아니라
/// "거기서 멈춤"으로 처리한다. message/text/varint 는 해당 wire type 인 필드의 첫 번째 등장 값.
/// </summary>
internal static class ProtoWire
{
    /// <summary>이보다 큰 토큰 카운터는 값이 아니라 센티널로 보고 버린다 (0 처리)</summary>
    public const ulong TokenCeiling = 1_000_000_000;
    public const ulong MinTimestampSeconds = 1_000_000_000;
    public const ulong MaxTimestampSeconds = 4_102_444_800;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TryVarint(ReadOnlyMemory<byte> data, int field, out ulong value)
    {
        var span = data.Span;
        int pos = 0;
        while (Next(span, ref pos, out var f, out var wire, out var v, out _, out _))
        {
            if (f == (ulong)field && wire == 0)
            {
                value = v;
                return true;
            }
        }
        value = 0;
        return false;
    }

    public static bool TryMessage(ReadOnlyMemory<byte> data, int field, out ReadOnlyMemory<byte> payload)
    {
        var span = data.Span;
        int pos = 0;
        while (Next(span, ref pos, out var f, out var wire, out _, out var start, out var length))
        {
            if (f == (ulong)field && wire == 2)
            {
                payload = data.Slice(start, length);
                return true;
            }
        }
        payload = ReadOnlyMemory<byte>.Empty;
        return false;
    }

    /// <summary>비어 있지 않은 올바른 UTF-8 문자열 필드</summary>
    public static string? Text(ReadOnlyMemory<byte> data, int field)
    {
        if (!TryMessage(data, field, out var payload) || payload.Length == 0) return null;
        try
        {
            var text = StrictUtf8.GetString(payload.Span);
            return text.Length == 0 ? null : text;
        }
        catch (ArgumentException)
        {
            return null; // DecoderFallbackException: 잘못된 UTF-8
        }
    }

    /// <summary>없으면 0 (정상 — cache_write 는 선언만 됨), 상한 이하면 값, 초과면 0 + discarded 증가</summary>
    public static long TokenCount(ReadOnlyMemory<byte> data, int field, ref int discarded)
    {
        if (!TryVarint(data, field, out var value)) return 0;
        if (value <= TokenCeiling) return (long)value;
        discarded++;
        return 0;
    }

    /// <summary>field 에 담긴 google.protobuf.Timestamp {1: seconds, 2: nanos}</summary>
    public static DateTimeOffset? Timestamp(ReadOnlyMemory<byte> data, int field) =>
        TryMessage(data, field, out var stamp) ? StampDate(stamp) : null;

    /// <summary>Timestamp 메시지 본문 → 날짜. seconds 는 [1e9, 4102444800] 이어야 하고 nanos 는 1e9 미만일 때만 사용.</summary>
    public static DateTimeOffset? StampDate(ReadOnlyMemory<byte> stamp)
    {
        if (!TryVarint(stamp, 1, out var seconds) || seconds < MinTimestampSeconds || seconds > MaxTimestampSeconds)
            return null;
        ulong nanos = TryVarint(stamp, 2, out var n) && n < 1_000_000_000UL ? n : 0UL;
        return DateTimeOffset.UnixEpoch.AddTicks((long)seconds * TimeSpan.TicksPerSecond + (long)(nanos / 100));
    }

    /// <summary>
    /// 다음 필드 하나. 고정폭(1: 8바이트, 5: 4바이트)은 건너뛰고, varint(0)·length-delimited(2)만 돌려준다.
    /// 끝이거나 해석할 수 없으면 false (field 0, 그룹 3/4, 길이 초과, 10바이트 넘는 varint).
    /// </summary>
    private static bool Next(ReadOnlySpan<byte> data, ref int pos, out ulong field, out int wireType,
        out ulong value, out int start, out int length)
    {
        field = 0;
        wireType = 0;
        value = 0;
        start = 0;
        length = 0;
        while (pos < data.Length)
        {
            if (!ReadVarint(data, ref pos, out var key)) return false;
            field = key >> 3;
            if (field == 0) return false;
            wireType = (int)(key & 7);
            switch (wireType)
            {
                case 0:
                    return ReadVarint(data, ref pos, out value);
                case 1:
                    if (data.Length - pos < 8) return false;
                    pos += 8;
                    continue;
                case 2:
                    if (!ReadVarint(data, ref pos, out var len) || len > (ulong)(data.Length - pos)) return false;
                    start = pos;
                    length = (int)len;
                    pos += length;
                    return true;
                case 5:
                    if (data.Length - pos < 4) return false;
                    pos += 4;
                    continue;
                default:
                    return false; // 그룹(3/4) 등: 우리가 생각한 메시지가 아님
            }
        }
        return false;
    }

    private static bool ReadVarint(ReadOnlySpan<byte> data, ref int pos, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
            if (shift > 63) return false; // varint 는 최대 10바이트
        }
        return false;
    }
}
