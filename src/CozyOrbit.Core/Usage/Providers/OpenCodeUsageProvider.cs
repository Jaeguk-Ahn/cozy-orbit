using System.Text.Json;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// OpenCode: SQLite (opencode.db — V2 session_message + V1 message) 와 구형 storage/message/**/*.json. 원본 spec §4.5.
/// 기본 루트: %USERPROFILE%\.local\share\opencode (XDG) 와 %LOCALAPPDATA%\opencode. OPENCODE_DATA_DIR(쉼표 목록)은 기본값을 대체.
/// V1·V2 는 같은 메시지 id 를 공유하므로 dedupKeepMax 로 합친다.
/// 원본 캐시는 증분 since 를 쓰지만 결과는 전체 재조회와 같으므로 여기서는 매번 전체 조회한다.
/// </summary>
public sealed class OpenCodeUsageProvider : IUsageProvider
{
    public string Id => "opencode";
    public string DisplayName => "OpenCode";

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = ScanRoots.EnvPaths(ctx.Env, "OPENCODE_DATA_DIR")
            ?? new List<string>
            {
                ctx.HomePath(".local", "share", "opencode"),
                Path.Combine(ctx.LocalAppData, "opencode"),
            };
        return ScanRoots.Union(defaults, ctx.CustomRootsRaw);
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run<IReadOnlyList<UsageEntry>>(() => Load(ctx), ctx.CancellationToken);

    private List<UsageEntry> Load(ProviderContext ctx)
    {
        var since = ctx.Since;
        var entries = new List<UsageEntry>();
        foreach (var root in ResolveRoots(ctx))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var database = PreferredDatabase(root);
            if (database != null) entries.AddRange(DatabaseEntries(database, since));

            var legacyRoot = Path.Combine(root, "storage", "message");
            foreach (var file in FileScan.Files(legacyRoot, FileScan.Extension(".json"), since))
            {
                var text = FileScan.ReadAllText(file);
                if (text == null) continue;
                using var doc = JsonValues.TryParse(text);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                var entry = ParseMessage(doc.RootElement, Path.GetFileNameWithoutExtension(file));
                if (entry != null) entries.Add(entry);
            }
        }
        return Dedup.KeepMax(entries.Where(e => e.Date >= since));
    }

    // ---------- DB discovery ----------

    /// <summary>루트가 .db 면 그대로, 아니면 opencode.db, 없으면 opencode-&lt;channel&gt;.db 중 경로순 첫 번째.</summary>
    internal static string? PreferredDatabase(string root)
    {
        if (root.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) return root;
        var standard = Path.Combine(root, "opencode.db");
        if (File.Exists(standard)) return standard;
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root)
                .Where(p => IsChannelDatabaseName(Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsChannelDatabaseName(string name)
    {
        const string prefix = "opencode-";
        const string suffix = ".db";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return false;
        if (name.Length <= prefix.Length + suffix.Length) return false;
        var channel = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return channel.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');
    }

    // ---------- DB rows ----------

    private static List<UsageEntry> DatabaseEntries(string database, DateTimeOffset since)
    {
        long cutoff = since.ToUnixTimeMilliseconds();
        var entries = new List<UsageEntry>();

        // V2: session_message (테이블이 없으면 실패 → 무시)
        var v2 = Query(database,
            "SELECT id, time_created, data FROM session_message WHERE type = 'assistant' AND time_created >= $cutoff",
            cmd => cmd.Parameters.AddWithValue("$cutoff", cutoff),
            ParseSessionMessageRow);
        if (v2 != null) entries.AddRange(v2);

        // V1: message (time_created 컬럼이 없는 구형 DB 는 전체 조회로 폴백)
        var v1 = Query(database,
            "SELECT id, session_id, data FROM message WHERE time_created >= $cutoff",
            cmd => cmd.Parameters.AddWithValue("$cutoff", cutoff),
            ParseMessageRow)
            ?? Query(database, "SELECT id, session_id, data FROM message", null, ParseMessageRow);
        if (v1 != null) entries.AddRange(v1);

        return Dedup.KeepMax(entries);
    }

    private static UsageEntry? ParseMessageRow(SqliteDataReader r)
    {
        var id = SqliteUtil.ColumnString(r, 0);
        var payload = SqliteUtil.ColumnString(r, 2);
        if (id == null || payload == null) return null;
        using var doc = JsonValues.TryParse(payload);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        return ParseMessage(doc.RootElement, id);
    }

    private static UsageEntry? ParseSessionMessageRow(SqliteDataReader r)
    {
        var id = SqliteUtil.ColumnString(r, 0);
        var payload = SqliteUtil.ColumnString(r, 2);
        if (id == null || payload == null) return null;
        using var doc = JsonValues.TryParse(payload);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        return ParseSessionMessage(doc.RootElement, id, SqliteUtil.ColumnDouble(r, 1));
    }

    // ---------- JSON parsers ----------

    /// <summary>V1 message.data / 구형 JSON 파일. tokens·time.created·modelID·providerID 필수.</summary>
    internal static UsageEntry? ParseMessage(JsonElement obj, string fallbackId)
    {
        var tokens = obj.Prop("tokens");
        if (!tokens.IsObject()) return null;
        var date = JsonValues.DateB(obj.Prop("time").Prop("created"));
        var model = JsonValues.NonEmptyString(obj.Prop("modelID"));
        if (date == null || model == null || JsonValues.NonEmptyString(obj.Prop("providerID")) == null) return null;
        var cache = tokens.Prop("cache");
        return UsageEntry.Make(
            "opencode|" + (JsonValues.NonEmptyString(obj.Prop("id")) ?? fallbackId),
            date.Value,
            model,
            input: JsonValues.IntB(tokens.Prop("input")),
            output: JsonValues.IntB(tokens.Prop("output")),
            cacheWrite: JsonValues.IntB(cache.Prop("write")),
            cacheRead: JsonValues.IntB(cache.Prop("read")),
            total: JsonValues.IntB(tokens.Prop("total")),
            cost: JsonValues.DoubleB(obj.Prop("cost")));
    }

    /// <summary>V2 session_message.data. reasoning 은 output 과 별도로 저장되므로 더한다. total 필드 없음.</summary>
    internal static UsageEntry? ParseSessionMessage(JsonElement obj, string fallbackId, double? createdAt)
    {
        var tokens = obj.Prop("tokens");
        var modelObj = obj.Prop("model");
        if (!tokens.IsObject() || !modelObj.IsObject()) return null;
        var date = createdAt is { } c ? UsageTime.FromEpochAuto(c) : null;
        var modelId = JsonValues.NonEmptyString(modelObj.Prop("id"));
        if (date == null || modelId == null || JsonValues.NonEmptyString(modelObj.Prop("providerID")) == null) return null;
        var cache = tokens.Prop("cache");
        return UsageEntry.Make(
            "opencode|" + fallbackId,
            date.Value,
            modelId,
            input: JsonValues.IntB(tokens.Prop("input")),
            output: JsonValues.IntB(tokens.Prop("output")) + JsonValues.IntB(tokens.Prop("reasoning")),
            cacheWrite: JsonValues.IntB(cache.Prop("write")),
            cacheRead: JsonValues.IntB(cache.Prop("read")),
            cost: JsonValues.DoubleB(obj.Prop("cost")));
    }

    // ---------- SQLite helper ----------

    /// <summary>
    /// 한 쿼리를 끝까지 실행. 파일이 없으면 빈 목록, 열기/준비/스텝 실패(BUSY 등)면 null (원본 query → nil).
    /// </summary>
    private static List<UsageEntry>? Query(string database, string sql, Action<SqliteCommand>? bind,
        Func<SqliteDataReader, UsageEntry?> map)
    {
        if (!File.Exists(database)) return new List<UsageEntry>();
        var conn = SqliteUtil.OpenReadOnly(database);
        if (conn == null)
        {
            AppLog.Warn($"opencode: cannot open {database}");
            return null;
        }
        try
        {
            using (conn)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                using var reader = cmd.ExecuteReader();
                var result = new List<UsageEntry>();
                while (reader.Read())
                {
                    var e = map(reader);
                    if (e != null) result.Add(e);
                }
                return result;
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            return null; // SQLITE_ERROR: 테이블/컬럼 없음 (구형·신형 스키마 차이) — 정상
        }
        catch (Exception ex)
        {
            AppLog.Warn($"opencode: query failed for {database}: {ex.Message}");
            return null;
        }
    }
}
