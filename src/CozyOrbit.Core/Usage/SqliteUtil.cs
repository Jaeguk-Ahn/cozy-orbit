using Microsoft.Data.Sqlite;

namespace CozyOrbit.Core.Usage;

/// <summary>
/// 다른 앱의 SQLite DB 를 읽기 전용으로 여는 도우미.
/// mode=ro 로 먼저 열어 WAL 꼬리까지 보고, 실패하면 immutable=1 로 재시도한다 (원본과 동일 순서).
/// 연결 풀을 끄고 매번 닫아 상대 앱의 쓰기를 방해하지 않는다.
/// </summary>
public static class SqliteUtil
{
    public static SqliteConnection? OpenReadOnly(string path, bool allowImmutableFallback = true, int busyTimeoutMs = 1000)
    {
        if (!File.Exists(path)) return null;
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri; // file:///C:/... (공백 등 퍼센트 인코딩)
        foreach (var suffix in allowImmutableFallback ? new[] { "?mode=ro", "?immutable=1" } : new[] { "?mode=ro" })
        {
            SqliteConnection? conn = null;
            try
            {
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = uri + suffix,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                    DefaultTimeout = Math.Max(1, busyTimeoutMs / 1000),
                }.ToString();
                conn = new SqliteConnection(cs);
                conn.Open();
                using (var pragma = conn.CreateCommand())
                {
                    pragma.CommandText = $"PRAGMA busy_timeout = {busyTimeoutMs};";
                    pragma.ExecuteNonQuery();
                }
                using (var probe = conn.CreateCommand())
                {
                    probe.CommandText = "SELECT count(*) FROM sqlite_master";
                    probe.ExecuteScalar();
                }
                return conn;
            }
            catch (Exception)
            {
                conn?.Dispose();
            }
        }
        return null;
    }

    /// <summary>테이블 존재 여부</summary>
    public static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() != null;
    }

    public static long ColumnLong(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return 0;
        try
        {
            var v = r.GetInt64(i);
            return v <= 0 ? 0 : Math.Min(v, JsonValues.MaxTokenValue);
        }
        catch { return 0; }
    }

    public static double? ColumnDouble(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        try { var d = r.GetDouble(i); return double.IsFinite(d) ? d : null; }
        catch { return null; }
    }

    public static string? ColumnString(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        try
        {
            var v = r.GetValue(i);
            return v switch
            {
                string s => s,
                byte[] b => System.Text.Encoding.UTF8.GetString(b),
                _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        catch { return null; }
    }

    public static byte[]? ColumnBlob(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        try
        {
            var v = r.GetValue(i);
            return v switch
            {
                byte[] b => b,
                string s => System.Text.Encoding.UTF8.GetBytes(s),
                _ => null,
            };
        }
        catch { return null; }
    }
}
