using System.Globalization;
using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// GitHub Copilot CLI: %USERPROFILE%\.copilot\session-store.db 의 assistant_usage_events. 원본 spec §4.9.
/// 행 하나 = API 호출 하나 (서브에이전트 호출 포함). input_tokens 는 캐시 읽기/쓰기를 포함한 전체 프롬프트이므로 뺀다.
/// COPILOT_HOME(쉼표 목록)은 기본값을 대체.
/// 원본은 id 워터마크로 증분 조회하지만(선택 사항), 여기서는 매번 created_at 하한으로 전체 재조회한다.
/// 결과는 같고, DB 재생성(id 재시작) 처리도 자연히 된다.
/// </summary>
public sealed class CopilotUsageProvider : IUsageProvider
{
    public string Id => "copilot";
    public string DisplayName => "Copilot";

    private const string RowsSql = """
        SELECT id, model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, created_at
        FROM assistant_usage_events
        WHERE created_at >= $cutoff
        """;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = ScanRoots.EnvPaths(ctx.Env, "COPILOT_HOME")
            ?? new List<string> { ctx.HomePath(".copilot") };
        return ScanRoots.Union(defaults, ctx.CustomRootsRaw);
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run<IReadOnlyList<UsageEntry>>(() => Load(ctx), ctx.CancellationToken);

    private List<UsageEntry> Load(ProviderContext ctx)
    {
        var entries = new List<UsageEntry>();
        var cutoff = DayCutoff(ctx.Since);
        foreach (var root in ResolveRoots(ctx))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var database = DatabasePath(root);
            var rows = DatabaseEntries(database, cutoff);
            if (rows != null) entries.AddRange(rows);
        }
        return Dedup.KeepMax(entries.Where(e => e.Date >= ctx.Since));
    }

    internal static string DatabasePath(string root) =>
        Path.GetFullPath(root.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ? root : Path.Combine(root, "session-store.db"));

    /// <summary>
    /// since 의 UTC 날짜 하루 전 00:00:00 ("yyyy-MM-dd 00:00:00"). created_at 은 텍스트라 사전순 비교이며,
    /// 어떤 UTC 오프셋이 붙은 행도 놓치지 않도록 하루 여유를 둔 거친 사전 필터다 (정확한 필터는 파싱 후).
    /// </summary>
    internal static string DayCutoff(DateTimeOffset since)
    {
        var utc = since.UtcDateTime;
        if (utc < DateTime.MinValue.AddDays(2)) return "0000-01-01 00:00:00";
        return utc.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00";
    }

    /// <summary>
    /// CLI 는 "...Z" ISO-8601 을, 컬럼 기본값 datetime('now') 는 "YYYY-MM-DD HH:MM:SS"(UTC)를 쓴다. 둘 다 처리.
    /// </summary>
    internal static DateTimeOffset? ParseCreatedAt(string raw)
    {
        var text = raw.Trim();
        if (text.Length < 19) return null;
        int space = text.IndexOf(' ');
        if (space >= 0) text = text.Substring(0, space) + "T" + text.Substring(space + 1);
        var time = text.Substring(11);
        if (!time.Contains('Z') && !time.Contains('+') && !time.Contains('-')) text += "Z";
        return UsageTime.ParseIso(text);
    }

    /// <summary>파일 없음 → 빈 목록, 열기/쿼리 실패(BUSY 등) → null (이번엔 이 DB 건너뜀).</summary>
    private static List<UsageEntry>? DatabaseEntries(string database, string cutoff)
    {
        if (!File.Exists(database)) return new List<UsageEntry>();
        var conn = SqliteUtil.OpenReadOnly(database);
        if (conn == null)
        {
            AppLog.Warn($"copilot: cannot open {database}");
            return null;
        }
        try
        {
            using (conn)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = RowsSql;
                cmd.Parameters.AddWithValue("$cutoff", cutoff);
                using var reader = cmd.ExecuteReader();
                var result = new List<UsageEntry>();
                while (reader.Read())
                {
                    var e = ParseRow(reader, database);
                    if (e != null) result.Add(e);
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"copilot: query failed for {database}: {ex.Message}");
            return null;
        }
    }

    private static UsageEntry? ParseRow(SqliteDataReader r, string database)
    {
        if (r.IsDBNull(0)) return null;
        long rowId = r.GetInt64(0);
        var rawDate = SqliteUtil.ColumnString(r, 6);
        if (rawDate == null) return null;
        var date = ParseCreatedAt(rawDate);
        if (date == null) return null;

        var model = SqliteUtil.ColumnString(r, 1)?.Trim();
        if (string.IsNullOrEmpty(model)) model = "unknown";
        long cacheRead = SqliteUtil.ColumnLong(r, 4);
        long cacheWrite = SqliteUtil.ColumnLong(r, 5);
        // input_tokens 는 전체 프롬프트 — 캐시 읽기/쓰기는 그 부분집합이므로 빼서 세 번 세지 않게 한다
        long input = Math.Max(0, SqliteUtil.ColumnLong(r, 2) - cacheRead - cacheWrite);
        // reasoning_tokens 는 output 의 내역이므로 더하지 않는다
        return UsageEntry.Make(
            // 행 id 는 DB 안에서만 유일 — 여러 DB 가 같은 id 로 합쳐지지 않도록 경로를 넣는다
            "copilot|" + database + "|" + rowId.ToString(CultureInfo.InvariantCulture),
            date.Value,
            model,
            input: input,
            output: SqliteUtil.ColumnLong(r, 3),
            cacheWrite: cacheWrite,
            cacheRead: cacheRead);
    }
}
