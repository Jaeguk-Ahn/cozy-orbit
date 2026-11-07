using Microsoft.Data.Sqlite;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Hermes Agent: %USERPROFILE%\.hermes\state.db 의 sessions 테이블. 원본 spec §4.6.
/// 세션 하나 = 레코드 하나 (started_at 날짜, 세션 누적 토큰). HERMES_HOME(쉼표 목록)은 기본값을 대체.
/// 세션 합계는 요청 단위 가격 계산이 불가능하므로 costUnavailable = true (기록된 비용만 사용).
/// </summary>
public sealed class HermesUsageProvider : IUsageProvider
{
    public string Id => "hermes";
    public string DisplayName => "Hermes Agent";

    private const string SessionsSql = """
        SELECT id, model, billing_provider, started_at, message_count,
               input_tokens, output_tokens, cache_read_tokens, cache_write_tokens,
               reasoning_tokens, estimated_cost_usd, actual_cost_usd
        FROM sessions
        WHERE model IS NOT NULL AND TRIM(model) != '' AND started_at >= $since
        """;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = ScanRoots.EnvPaths(ctx.Env, "HERMES_HOME")
            ?? new List<string> { ctx.HomePath(".hermes") };
        return ScanRoots.Union(defaults, ctx.CustomRootsRaw);
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run<IReadOnlyList<UsageEntry>>(() => Load(ctx), ctx.CancellationToken);

    private List<UsageEntry> Load(ProviderContext ctx)
    {
        var entries = new List<UsageEntry>();
        foreach (var root in ResolveRoots(ctx))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var database = root.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ? root : Path.Combine(root, "state.db");
            var rows = DatabaseEntries(database, ctx.Since);
            if (rows != null) entries.AddRange(rows.Where(e => e.Date >= ctx.Since));
        }
        // 원본: 루트 간 같은 id 는 처음 것만 (keep-max 아님)
        return Dedup.KeepFirst(entries);
    }

    /// <summary>파일 없음 → 빈 목록, 열기/쿼리 실패 → null (이번엔 이 DB 건너뜀).</summary>
    private static List<UsageEntry>? DatabaseEntries(string database, DateTimeOffset since)
    {
        if (!File.Exists(database)) return new List<UsageEntry>();
        var conn = SqliteUtil.OpenReadOnly(database);
        if (conn == null)
        {
            AppLog.Warn($"hermes: cannot open {database}");
            return null;
        }
        try
        {
            using (conn)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SessionsSql;
                cmd.Parameters.AddWithValue("$since", since.ToUnixTimeSeconds());
                using var reader = cmd.ExecuteReader();
                var result = new List<UsageEntry>();
                while (reader.Read())
                {
                    var e = ParseRow(reader);
                    if (e != null) result.Add(e);
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"hermes: query failed for {database}: {ex.Message}");
            return null;
        }
    }

    private static UsageEntry? ParseRow(SqliteDataReader r)
    {
        var id = SqliteUtil.ColumnString(r, 0)?.Trim();
        var model = SqliteUtil.ColumnString(r, 1)?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(model)) return null;
        // started_at: REAL 초 (1e11 이상이면 ms 로 자동 판별)
        var startedAt = SqliteUtil.ColumnDouble(r, 3);
        var date = startedAt is { } s ? UsageTime.FromEpochAuto(s) : null;
        if (date == null) return null;

        double? estimatedCost = SqliteUtil.ColumnDouble(r, 10);
        double? actualCost = SqliteUtil.ColumnDouble(r, 11);
        return UsageEntry.Make(
            "hermes|" + id,
            date.Value,
            model,
            input: SqliteUtil.ColumnLong(r, 5),
            output: SqliteUtil.ColumnLong(r, 6) + SqliteUtil.ColumnLong(r, 9), // reasoning 은 별도 컬럼 → output 에 더함
            cacheWrite: SqliteUtil.ColumnLong(r, 8),
            cacheRead: SqliteUtil.ColumnLong(r, 7),
            cost: actualCost ?? estimatedCost,
            costUnavailable: true,
            costIsEstimate: actualCost == null);
    }
}
