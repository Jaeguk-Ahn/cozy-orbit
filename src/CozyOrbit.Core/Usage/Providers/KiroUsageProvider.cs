using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Kiro CLI (spec §4.10). 토큰 수는 추정치: UTF-8 바이트 / 4. 비용은 항상 "알 수 없음".
///  (a) 2.20 이전 SQLite: &lt;home&gt;/data.sqlite3 의 conversations_v2 / conversations (대화 전체 JSON 을 매 턴 덮어씀)
///  (b) CLI 2.20 JSONL: &lt;root&gt;/**/cli/&lt;id&gt;.jsonl (+ 동반 &lt;id&gt;.json)
///  (c) v3/IDE JSONL: &lt;root&gt;/**/messages.jsonl (+ 형제 session.json)
/// Kiro 는 /clear·압축 때 기록을 *삭제*하므로, 이전 스캔 결과와 keep-max 로 합쳐 이미 센 사용량이 줄지 않게 한다
/// (월이 바뀌면 그 기억을 버린다).
/// </summary>
public sealed class KiroUsageProvider : IUsageProvider
{
    private const int BytesPerToken = 4;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly object _gate = new();
    // 이전 스캔들의 병합 결과 (삭제된 행도 계속 센다)
    private List<UsageEntry> _known = new();
    // 파일 서명 (mtime·크기; SQLite 는 db + -wal). 바뀌지 않은 파일은 다시 읽지 않는다.
    private Dictionary<string, (DateTime MtimeUtc, long Size)> _signatures = new(StringComparer.OrdinalIgnoreCase);
    private string? _monthKey;
    private string? _rootsKey;
    private DateTimeOffset? _lastSince;

    public string Id => "kiro";
    public string DisplayName => "Kiro";

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        // kiro-cli 는 Rust dirs::data_local_dir() 를 쓰므로 %LOCALAPPDATA% 가 유력. %APPDATA% 도 기본으로 함께 본다.
        var sqliteHomes = ScanRoots.EnvPaths(ctx.Env, "KIRO_CLI_HOME")
                          ?? new List<string>
                          {
                              Path.Combine(ctx.LocalAppData, "kiro-cli"),
                              Path.Combine(ctx.RoamingAppData, "kiro-cli"),
                          };
        var kiroHomes = ScanRoots.EnvPaths(ctx.Env, "KIRO_HOME")
                        ?? new List<string> { Path.Combine(ctx.Home, ".kiro") };
        var defaults = sqliteHomes.Concat(kiroHomes.Select(h => Path.Combine(h, "sessions"))).ToList();
        return ScanRoots.Union(defaults, ctx.CustomRootsRaw);
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run(() => Load(ctx), ctx.CancellationToken);

    private IReadOnlyList<UsageEntry> Load(ProviderContext ctx)
    {
        var roots = ResolveRoots(ctx);
        lock (_gate)
        {
            var month = UsageTime.MonthKey(ctx.Now);
            var rootsKey = string.Join("\n", roots);
            if (month != _monthKey || rootsKey != _rootsKey)
            {
                // 월 변경(원본 캐시 드롭) 또는 루트 변경(설정 저장): 기억한 엔트리와 서명을 함께 버린다
                _known = new List<UsageEntry>();
                _signatures = new Dictionary<string, (DateTime MtimeUtc, long Size)>(StringComparer.OrdinalIgnoreCase);
                _monthKey = month;
                _rootsKey = rootsKey;
                _lastSince = null;
            }
            // 하한이 더 이르게 바뀌면 건너뛴 파일에 창 밖이던 턴이 있을 수 있으니 전부 다시 읽는다
            if (_lastSince is { } prev && ctx.Since < prev) _signatures.Clear();
            _lastSince = ctx.Since;

            var (loaded, signatures) = Scan(roots, ctx.Since, _signatures, ctx.CancellationToken);
            _known = Dedup.KeepMax(_known.Concat(loaded));
            _signatures = signatures;
            return _known.Where(e => e.Date >= ctx.Since).ToList();
        }
    }

    private static (List<UsageEntry> Entries, Dictionary<string, (DateTime MtimeUtc, long Size)> Signatures) Scan(
        IReadOnlyList<string> roots, DateTimeOffset since,
        Dictionary<string, (DateTime MtimeUtc, long Size)> known, CancellationToken ct)
    {
        var entries = new List<UsageEntry>();
        var signatures = new Dictionary<string, (DateTime MtimeUtc, long Size)>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // 루트가 겹쳐도 파일은 한 번만
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            // 모든 루트에서 두 저장소를 다 시도한다 (추가 폴더는 DB 존재가 아니라 레이아웃으로 분류)
            var database = root.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase) ? root : Path.Combine(root, "data.sqlite3");
            database = FullPath(database);
            if (seen.Add(database))
            {
                var (dbEntries, signature) = ScanDatabase(database, since, known);
                entries.AddRange(dbEntries);
                if (signature is { } s) signatures[database] = s;
            }
            foreach (var file in JsonlSessionFiles(root))
            {
                if (!seen.Add(file)) continue;
                ct.ThrowIfCancellationRequested();
                var (fileEntries, signature) = ScanJsonlFile(file, since, known);
                entries.AddRange(fileEntries);
                if (signature is { } s) signatures[file] = s;
            }
        }
        return (Dedup.KeepMax(entries), signatures);
    }

    private static string FullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception) { return path; }
    }

    // ---------------------------------------------------------------- (a) SQLite

    private static (List<UsageEntry> Entries, (DateTime MtimeUtc, long Size)? Signature) ScanDatabase(
        string database, DateTimeOffset since, Dictionary<string, (DateTime MtimeUtc, long Size)> known)
    {
        var entries = new List<UsageEntry>();
        if (!File.Exists(database)) return (entries, null);
        // 읽기 *전에* 서명: 읽는 도중 들어온 커밋은 다음 폴링에서 서명이 달라 다시 읽힌다
        var signature = FileScan.SqliteSignature(database);
        if (signature is { } sig && known.TryGetValue(database, out var remembered) && remembered == sig)
            return (entries, sig); // 변경 없음 → 빈 결과 (이전 엔트리는 병합으로 유지)

        List<(string? Id, string Value)>? v2Rows;
        List<string>? v1Rows;
        using (var conn = SqliteUtil.OpenReadOnly(database))
        {
            if (conn == null) return (entries, null); // 열기 실패: 서명을 남기지 않아 다음 폴링에 재시도
            v2Rows = QueryV2(conn); // kiro-cli < 2.0.1: 대화당 한 행
            v1Rows = QueryV1(conn); // kiro-cli 2.0.1+: 작업 폴더당 한 행, 대화 id 는 JSON 안
        }
        // 둘 다 실패(BUSY·손상 등) → 이번엔 건너뜀 (서명 없음)
        if (v2Rows == null && v1Rows == null) return (entries, null);

        if (v2Rows != null)
        {
            foreach (var (rowId, value) in v2Rows)
            {
                using var doc = JsonValues.TryParse(value);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                var conversationId = rowId ?? JsonValues.NonEmptyString(doc.RootElement.Prop("conversation_id")) ?? database;
                AddTurnEntries(entries, conversationId, doc.RootElement, since);
            }
        }
        if (v1Rows != null)
        {
            foreach (var value in v1Rows)
            {
                using var doc = JsonValues.TryParse(value);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                // conversation_id 가 없는 v1 행은 키를 만들 수 없어 버린다
                var conversationId = JsonValues.NonEmptyString(doc.RootElement.Prop("conversation_id"));
                if (conversationId == null) continue;
                AddTurnEntries(entries, conversationId, doc.RootElement, since);
            }
        }
        return (entries, signature);
    }

    private static List<(string? Id, string Value)>? QueryV2(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT conversation_id, value FROM conversations_v2";
            using var reader = cmd.ExecuteReader();
            var rows = new List<(string? Id, string Value)>();
            while (reader.Read())
            {
                var value = SqliteUtil.ColumnString(reader, 1);
                if (value == null) continue;
                rows.Add((SqliteUtil.ColumnString(reader, 0), value));
            }
            return rows;
        }
        catch (Exception) { return null; } // 테이블 없음 / BUSY / 손상 → 이 세대는 실패
    }

    private static List<string>? QueryV1(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM conversations";
            using var reader = cmd.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                var value = SqliteUtil.ColumnString(reader, 0);
                if (value != null) rows.Add(value);
            }
            return rows;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 대화 history 를 걸으며 턴별 엔트리. 매 요청이 대화 전체를 다시 보내므로 input = (누적 history + 이번 user) / 4.
    /// request_metadata.user_prompt_length 는 새로 입력한 바이트뿐이라 쓰지 않는다.
    /// </summary>
    private static void AddTurnEntries(List<UsageEntry> output, string conversationId, JsonElement conversation, DateTimeOffset since)
    {
        var start = output.Count;
        try { AddTurnEntriesCore(output, conversationId, conversation, since); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            output.RemoveRange(start, output.Count - start); // 손상된 행은 통째로 건너뜀
        }
    }

    private static void AddTurnEntriesCore(List<UsageEntry> output, string conversationId, JsonElement conversation, DateTimeOffset since)
    {
        if (conversation.Prop("history") is not { ValueKind: JsonValueKind.Array } history) return;
        // latest_summary 는 압축으로 지워진 턴을 대신해 계속 재전송된다 → 누적의 시작값.
        // 원본 그대로: 키가 없으면 숫자 0 → "0" → 1 바이트, JSON null 이면 0 바이트.
        long cumulative = conversation.TryGetProperty("latest_summary", out var summary) ? ValueBytes(summary) : 1;
        foreach (var turn in history.EnumerateArray())
        {
            if (turn.ValueKind != JsonValueKind.Object) continue;
            long userBytes = FieldBytes(turn, "user");
            var entry = TurnEntry(conversationId, turn, cumulative + userBytes, since);
            if (entry != null) output.Add(entry);
            // 건너뛴 턴(타임스탬프 없음, 창 밖)도 이후 요청에 재전송되므로 항상 누적
            cumulative += userBytes + FieldBytes(turn, "assistant");
        }
    }

    private static UsageEntry? TurnEntry(string conversationId, JsonElement turn, long promptBytes, DateTimeOffset since)
    {
        if (turn.Prop("request_metadata") is not { ValueKind: JsonValueKind.Object } meta) return null;
        // 타임스탬프 없는 턴은 안정적인 id 를 만들 수 없어 건너뛴다
        var tsElement = meta.Prop("request_start_timestamp_ms");
        if (JsonValues.DoubleB(tsElement) is not { } raw || raw <= 0) return null;
        // 원본은 1e30 같은 날짜도 Date 로 만들지만 DateTimeOffset 범위 밖이면 여기서는 건너뛴다
        if (JsonValues.DateB(tsElement) is not { } date || date < since) return null;
        return UsageEntry.Make(
            "kiro|" + conversationId + "|" + Int64Millis(raw).ToString(Inv),
            date,
            JsonValues.NonEmptyString(meta.Prop("model_id")) ?? "unknown",
            input: promptBytes / BytesPerToken,
            output: JsonValues.IntB(meta.Prop("response_size")) / BytesPerToken,
            cacheWrite: 0, cacheRead: 0,
            costUnavailable: true);
    }

    /// <summary>turn.user / turn.assistant 의 바이트: 문자열이면 UTF-8 길이, 객체면 "images" 를 뺀 값들의 합.</summary>
    private static long FieldBytes(JsonElement turn, string key)
    {
        if (!turn.TryGetProperty(key, out var value)) return 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return Utf8Length(value);
            case JsonValueKind.Object:
                long total = 0;
                foreach (var prop in value.EnumerateObject())
                    if (prop.Name != "images") total += ValueBytes(prop.Value);
                return total;
            default:
                return 0;
        }
    }

    /// <summary>JSON 값의 바이트 추정: 문자열 UTF-8 길이, 숫자는 문자열화 길이, bool 1, 배열/객체는 자식 합, null 0.</summary>
    private static long ValueBytes(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return Utf8Length(value);
            case JsonValueKind.Number:
                return NumberText(value).Length;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return 1; // NSNumber(bool).stringValue = "1"/"0"
            case JsonValueKind.Array:
            {
                long total = 0;
                foreach (var item in value.EnumerateArray()) total += ValueBytes(item);
                return total;
            }
            case JsonValueKind.Object:
            {
                long total = 0;
                foreach (var prop in value.EnumerateObject()) total += ValueBytes(prop.Value);
                return total;
            }
            default:
                return 0;
        }
    }

    /// <summary>NSNumber.stringValue 근사: 정수는 그대로, 실수는 최단 왕복 표현</summary>
    private static string NumberText(JsonElement number)
    {
        if (number.TryGetInt64(out var l)) return l.ToString(Inv);
        if (number.TryGetDouble(out var d)) return d.ToString(Inv);
        return number.GetRawText();
    }

    private static long Utf8Length(JsonElement stringValue) => Encoding.UTF8.GetByteCount(stringValue.GetString() ?? "");

    /// <summary>원본 int64Millis: 0 쪽으로 자르고, Int64 범위를 넘으면 포화</summary>
    private static long Int64Millis(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return 0;
        var rounded = Math.Truncate(value);
        if (rounded >= 9.223372036854776e18) return long.MaxValue;
        return (long)rounded;
    }

    // ---------------------------------------------------------------- (b)(c) JSONL

    /// <summary>레이아웃 기반: messages.jsonl 또는 부모 폴더가 "cli" 인 *.jsonl. 루트 자신이 그런 파일이면 그것만.</summary>
    private static List<string> JsonlSessionFiles(string root)
    {
        if (File.Exists(root))
            return IsJsonlSessionFile(root) ? new List<string> { FullPath(root) } : new List<string>();
        return FileScan.Files(root, FileScan.Extension(".jsonl")).Where(IsJsonlSessionFile).ToList();
    }

    private static bool IsJsonlSessionFile(string path)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(name, "messages.jsonl", StringComparison.OrdinalIgnoreCase)) return true;
        return name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "cli", StringComparison.OrdinalIgnoreCase);
    }

    private static (List<UsageEntry> Entries, (DateTime MtimeUtc, long Size)? Signature) ScanJsonlFile(
        string file, DateTimeOffset since, Dictionary<string, (DateTime MtimeUtc, long Size)> known)
    {
        var signature = FileScan.Signature(file);
        if (signature is not { } sig) return (new List<UsageEntry>(), null);
        if (known.TryGetValue(file, out var remembered) && remembered == sig) return (new List<UsageEntry>(), sig);
        try
        {
            var entries = string.Equals(Path.GetFileName(file), "messages.jsonl", StringComparison.OrdinalIgnoreCase)
                ? V3Entries(file, since)
                : CliEntries(file, since);
            return (entries, sig);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            // 범위를 넘는 숫자 등 손상된 파일: 기여 없음 (파일이 바뀔 때까지 다시 읽지 않음)
            return (new List<UsageEntry>(), sig);
        }
    }

    private static JsonDocument? ReadJsonFile(string path)
    {
        var text = FileScan.ReadAllText(path);
        return text == null ? null : JsonValues.TryParse(text);
    }

    /// <summary>CLI 2.20 JSONL: {version, kind: Prompt|AssistantMessage|ToolResults|Clear, data}</summary>
    private static List<UsageEntry> CliEntries(string path, DateTimeOffset since)
    {
        string sessionId;
        string model;
        using (var companion = ReadJsonFile(Path.ChangeExtension(path, ".json")))
        {
            JsonElement? c = companion?.RootElement;
            sessionId = JsonValues.NonEmptyString(c.Prop("session_id")) ?? Path.GetFileNameWithoutExtension(path);
            model = JsonValues.NonEmptyString(
                c.Prop("session_state").Prop("rts_model_state").Prop("model_info").Prop("model_id")) ?? "unknown";
        }

        var entries = new List<UsageEntry>();
        long historyBytes = 0, promptBytes = 0, assistantBytes = 0, toolBytes = 0;
        double? promptRaw = null;
        DateTimeOffset? promptDate = null;
        bool started = false;

        void Flush()
        {
            // 타임스탬프가 없거나 창 밖이면 엔트리는 만들지 않지만 텍스트는 history 에 남긴다
            if (started && promptDate is { } date && date >= since)
            {
                long input = (historyBytes + promptBytes + toolBytes) / BytesPerToken;
                long output = assistantBytes / BytesPerToken;
                long millis = TimestampMillis(promptRaw, date);
                var e = UsageEntry.Make(FormattableString.Invariant($"kiro|cli|{sessionId}|{millis}"),
                    date, model, input, output, 0, 0, costUnavailable: true);
                if (e != null) entries.Add(e);
            }
            historyBytes += promptBytes + assistantBytes + toolBytes;
            promptBytes = 0;
            promptRaw = null;
            promptDate = null;
            assistantBytes = 0;
            toolBytes = 0;
        }

        foreach (var line in FileScan.ReadLines(path))
        {
            using var doc = JsonValues.TryParse(line);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) continue;
            var obj = doc.RootElement;
            var kind = JsonValues.NonEmptyString(obj.Prop("kind")) ?? "";
            var data = obj.Prop("data");
            switch (kind)
            {
                case "Prompt":
                {
                    if (started) Flush();
                    started = true;
                    promptBytes = TextBytes(data.Prop("content"));
                    var ts = data.Prop("meta").Prop("timestamp"); // 초 단위 실수
                    promptRaw = JsonValues.DoubleB(ts);
                    promptDate = JsonValues.FlexibleDate(ts);
                    assistantBytes = 0;
                    toolBytes = 0;
                    break;
                }
                case "AssistantMessage":
                    assistantBytes += TextBytes(data.Prop("content"));
                    break;
                case "ToolResults":
                    toolBytes += TextBytes(data.Prop("content"));
                    break;
                case "Clear":
                    Flush();
                    started = false;
                    historyBytes = 0;
                    break;
            }
        }
        Flush();
        return entries;
    }

    private static long TimestampMillis(double? raw, DateTimeOffset date)
    {
        if (raw is { } v && double.IsFinite(v) && v > 0)
            return Int64Millis(v < 1_000_000_000_000 ? v * 1000 : v);
        return Math.Max(0, date.ToUnixTimeMilliseconds());
    }

    /// <summary>v3 / IDE: messages.jsonl + 형제 session.json. 구조형 {payload:{type}} 과 평면형 {role,content} 공존.</summary>
    private static List<UsageEntry> V3Entries(string path, DateTimeOffset since)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        string sessionId;
        string model;
        DateTimeOffset? fallbackDate;
        using (var session = ReadJsonFile(Path.Combine(dir, "session.json")))
        {
            JsonElement? s = session?.RootElement;
            sessionId = JsonValues.NonEmptyString(s.Prop("id")) ?? Path.GetFileName(dir);
            model = JsonValues.NonEmptyString(s.Prop("modelId")) ?? "unknown";
            fallbackDate = JsonValues.FlexibleDate(s.Prop("createdAt")) ?? JsonValues.FlexibleDate(s.Prop("lastModifiedAt"));
        }

        var entries = new List<UsageEntry>();
        long historyBytes = 0, promptBytes = 0, assistantBytes = 0;
        DateTimeOffset? promptDate = null;
        bool started = false;
        int turnIndex = 0;

        void Flush()
        {
            bool hadContent = started && promptBytes + assistantBytes > 0;
            if (hadContent && (promptDate ?? fallbackDate) is { } date && date >= since)
            {
                long input = (historyBytes + promptBytes) / BytesPerToken;
                long output = assistantBytes / BytesPerToken;
                var e = UsageEntry.Make(FormattableString.Invariant($"kiro|v3|{sessionId}|{turnIndex}"),
                    date, model, input, output, 0, 0, costUnavailable: true);
                if (e != null) entries.Add(e);
            }
            historyBytes += promptBytes + assistantBytes;
            promptBytes = 0;
            promptDate = null;
            assistantBytes = 0;
            // 창 밖 턴도 번호를 올려 이후 id 가 창과 무관하게 안정적이게
            if (hadContent) turnIndex++;
        }

        void StartUser(long bytes, DateTimeOffset? eventDate)
        {
            if (started) Flush();
            started = true;
            promptBytes = bytes;
            promptDate = eventDate;
            assistantBytes = 0;
        }

        void AddAssistant(long bytes, DateTimeOffset? eventDate)
        {
            assistantBytes += bytes;
            if (!started && assistantBytes > 0) started = true;
            promptDate ??= eventDate;
        }

        foreach (var line in FileScan.ReadLines(path))
        {
            using var doc = JsonValues.TryParse(line);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) continue;
            var obj = doc.RootElement;
            if (obj.Prop("payload") is { ValueKind: JsonValueKind.Object } payload
                && JsonValues.NonEmptyString(payload.Prop("type")) is { } payloadType)
            {
                var eventDate = JsonValues.FlexibleDate(obj.Prop("timestamp"));
                switch (payloadType)
                {
                    case "user":
                        StartUser(TextBytes(payload.Prop("content")), eventDate);
                        break;
                    case "assistant":
                        AddAssistant(TextBytes(payload.Prop("content")), eventDate);
                        break;
                    case "tool_call":
                        // tool 호출 인자도 출력 바이트로 센다 (tokscale 와 동일)
                        if (payload.TryGetProperty("args", out var args))
                            assistantBytes += args.ValueKind == JsonValueKind.String ? Utf8Length(args) : ValueBytes(args);
                        if (!started && assistantBytes > 0) started = true;
                        break;
                    case "tool_result":
                        promptBytes += TextBytes(payload.Prop("content"));
                        break;
                    case "turn_end":
                        promptDate ??= eventDate;
                        Flush();
                        started = false;
                        break;
                    default:
                        break; // usage_summary 의 credits 는 달러가 아니다
                }
            }
            else if (JsonValues.NonEmptyString(obj.Prop("role")) is { } role)
            {
                var eventDate = JsonValues.FlexibleDate(obj.Prop("timestamp"));
                if (role is "user" or "human" or "prompt")
                    StartUser(TextBytes(obj.Prop("content")), eventDate);
                else if (role is "assistant" or "bot")
                    AddAssistant(TextBytes(obj.Prop("content")), eventDate);
            }
        }
        Flush();
        return entries;
    }

    /// <summary>
    /// 내용 블록의 텍스트 바이트. 문자열/배열은 그대로 합산, kind 가 있는 블록은 종류별
    /// (text / toolUse.input / toolResult.content / thinking.text / json), 그 외 kind 는 0 (이미지·서명 등 제외).
    /// kind 없는 객체는 content, text, data 중 처음 있는 키 (JSON null 도 "있음").
    /// </summary>
    private static long TextBytes(JsonElement? value)
    {
        if (value is not { } v) return 0;
        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                return Utf8Length(v);
            case JsonValueKind.Array:
            {
                long total = 0;
                foreach (var item in v.EnumerateArray()) total += TextBytes(item);
                return total;
            }
            case JsonValueKind.Object:
            {
                if (v.Prop("kind") is { ValueKind: JsonValueKind.String } kindElement)
                {
                    var data = v.Prop("data");
                    switch (kindElement.GetString())
                    {
                        case "text":
                            return TextBytes(data);
                        case "toolUse":
                            return data.Prop("input") is { } input ? ValueBytes(input) : 0;
                        case "toolResult":
                            return TextBytes(data.Prop("content"));
                        case "thinking":
                            return data.Prop("text") is { ValueKind: JsonValueKind.String } text ? Utf8Length(text) : 0;
                        case "json":
                            return data is { } json ? ValueBytes(json) : 0;
                        default:
                            return 0;
                    }
                }
                foreach (var key in TextKeys)
                    if (v.TryGetProperty(key, out var child)) return TextBytes(child);
                return 0;
            }
            default:
                return 0;
        }
    }

    private static readonly string[] TextKeys = { "content", "text", "data" };
}
