using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Aside: %USERPROFILE%\.aside\u\&lt;user&gt;\state.db 의 session_turns. 원본 spec §4.13.
/// 턴 하나 = 레코드 하나, 마지막 활동 시각(finished_at ?? last_message_timestamp, epoch 초)에 귀속.
/// 비용은 기록된 usage.cost.total 만 사용 (sessions.model 은 세션의 "현재" 모델이라 가격표 추정 금지).
///
/// Aside 는 턴을 제자리 갱신하고 세션 삭제 시 턴을 지운다. 그래서 원본 캐시처럼 이전 결과와
/// dedupKeepMax 로 병합해, 이미 센 사용량이 줄지 않게 한다 (월이 바뀌거나 Invalidate() 하면 초기화).
/// </summary>
public sealed class AsideUsageProvider : IUsageProvider
{
    public string Id => "aside";
    public string DisplayName => "Aside";

    private const string TurnsSql = """
        SELECT t.id, t.token_usage, COALESCE(t.finished_at, t.last_message_timestamp), s.model
        FROM session_turns t LEFT JOIN sessions s ON s.id = t.session_id
        WHERE COALESCE(t.finished_at, t.last_message_timestamp) >= $since
        """;

    private readonly object _gate = new();
    private List<UsageEntry> _merged = new();
    private string? _mergedMonth;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx) =>
        ScanRoots.Union(new[] { ctx.HomePath(".aside", "u") }, ctx.CustomRootsRaw);

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run<IReadOnlyList<UsageEntry>>(() => Load(ctx), ctx.CancellationToken);

    /// <summary>병합 캐시를 버린다 (설정 저장 등). 다음 스캔이 진실이 된다.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _merged = new List<UsageEntry>();
            _mergedMonth = null;
        }
    }

    private List<UsageEntry> Load(ProviderContext ctx)
    {
        var loaded = new List<UsageEntry>();
        var skipped = new List<string>();
        foreach (var db in Databases(ResolveRoots(ctx)))
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var rows = DatabaseEntries(db, ctx.Since);
            if (rows == null) skipped.Add(db);
            else loaded.AddRange(rows);
        }
        if (skipped.Count > 0) AppLog.Warn("aside: skipped unreadable state.db: " + string.Join(", ", skipped));

        var month = UsageTime.MonthKey(ctx.Now);
        List<UsageEntry> merged;
        lock (_gate)
        {
            var existing = _mergedMonth == month ? _merged : new List<UsageEntry>();
            merged = Dedup.KeepMax(existing.Concat(loaded));
            _merged = merged;
            _mergedMonth = month;
        }
        return merged.Where(e => e.Date >= ctx.Since).ToList();
    }

    /// <summary>각 루트의 state.db 와 직속 하위 폴더의 state.db (링크 해석, 정렬, 중복 제거).</summary>
    internal static List<string> Databases(IEnumerable<string> roots)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var dirs = new List<string> { root };
            try
            {
                if (Directory.Exists(root)) dirs.AddRange(Directory.EnumerateDirectories(root));
            }
            catch (Exception)
            {
                // 권한 없음 등 — 루트 자신만 확인
            }
            foreach (var dir in dirs)
            {
                var db = ResolveLink(Path.Combine(dir, "state.db"));
                if (File.Exists(db) && seen.Add(db)) found.Add(db);
            }
        }
        return found.ToList();
    }

    private static string ResolveLink(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var target = new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true);
            return target != null ? Path.GetFullPath(target.FullName) : full;
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>열기/준비/스텝 실패 → null (부분 행은 버리고 이 DB 만 건너뜀). 예외를 던지지 않는다.</summary>
    private static List<UsageEntry>? DatabaseEntries(string database, DateTimeOffset since)
    {
        var conn = SqliteUtil.OpenReadOnly(database, allowImmutableFallback: false, busyTimeoutMs: 1000);
        if (conn == null) return null;
        try
        {
            using (conn)
            using (var cmd = conn.CreateCommand())
            {
                // 같은 경로에 새로 만들어진 state.db 는 AUTOINCREMENT 가 1 부터 다시 시작하므로 파일 식별자를 id 에 넣는다
                var store = "aside|" + database + "#" + FileIdentity(database);
                cmd.CommandText = TurnsSql;
                cmd.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds() / 1000.0);
                using var reader = cmd.ExecuteReader();
                var rows = new List<UsageEntry>();
                while (reader.Read())
                {
                    var e = ParseRow(reader, store);
                    if (e != null) rows.Add(e);
                }
                return rows;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"aside: query failed for {database}: {ex.Message}");
            return null;
        }
    }

    private static UsageEntry? ParseRow(SqliteDataReader r, string store)
    {
        if (r.IsDBNull(0)) return null;
        long turnId = r.GetInt64(0);
        var usageText = SqliteUtil.ColumnString(r, 1);
        if (usageText == null) return null;
        using var usageDoc = JsonValues.TryParse(usageText);
        if (usageDoc == null || usageDoc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var usage = usageDoc.RootElement;

        var seconds = SqliteUtil.ColumnDouble(r, 2);
        var date = seconds is { } s ? UsageTime.FromEpochSeconds(s) : null;
        if (date == null) return null;

        var model = ModelId(SqliteUtil.ColumnString(r, 3)) ?? "aside";

        double? cost = usage.Prop("cost").Prop("total") is { ValueKind: JsonValueKind.Number } c ? JsonValues.DoubleOrNull(c) : null;
        bool granular = new[] { "input", "output", "cacheRead", "cacheWrite" }
            .Any(k => JsonValues.IsNumber(usage.Prop(k)));
        long input = Tokens(granular ? usage.Prop("input") : usage.Prop("totalTokens"));
        long output = Tokens(usage.Prop("output"));
        long cacheWrite = Tokens(usage.Prop("cacheWrite"));
        long cacheRead = Tokens(usage.Prop("cacheRead"));
        // 응답 전에 중단된 0 토큰 턴은 사용량이 아니다
        if (input + output + cacheWrite + cacheRead <= 0) return null;

        return UsageEntry.Create(
            store + ":" + turnId.ToString(CultureInfo.InvariantCulture),
            date.Value, model, input, output, cacheWrite, cacheRead,
            explicitCost: cost, costUnavailable: true);
    }

    /// <summary>sessions.model 은 JSON 텍스트 — modelId 문자열 (없거나 비면 null)</summary>
    private static string? ModelId(string? json)
    {
        if (json == null) return null;
        using var doc = JsonValues.TryParse(json);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        return JsonValues.NonEmptyString(doc.RootElement.Prop("modelId"));
    }

    /// <summary>숫자 &gt; 0 만, 1e18 상한, 그 외 0</summary>
    private static long Tokens(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } v ? JsonValues.ClampToken(JsonValues.DoubleOrNull(v) ?? 0) : 0;

    // ---------- 파일 식별자 (원본의 inode 대응) ----------

    /// <summary>
    /// 원본은 stat 의 inode 를 쓴다. Windows 에서는 NTFS 파일 인덱스(GetFileInformationByHandle 의
    /// nFileIndexHigh/Low, MFT 레코드 번호 + 재사용 시퀀스)를 쓰고, 실패하거나 Windows 가 아니면
    /// 파일 생성 시각(CreationTimeUtc ticks)으로 대신한다.
    /// 생성 시각만 쓰면 NTFS "file tunneling"(15초 안에 같은 이름으로 다시 만들면 생성 시각 복원) 때문에
    /// 재생성을 놓칠 수 있어 파일 인덱스를 우선한다. 실패하면 "0" (원본과 같음).
    /// </summary>
    internal static string FileIdentity(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (GetFileInformationByHandle(handle, out var info))
                {
                    ulong index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
                    return index.ToString(CultureInfo.InvariantCulture);
                }
            }
            return File.GetCreationTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return "0";
        }
    }

#pragma warning disable CS0649, CS0169 // 마샬링이 채우는 필드
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow, CreationTimeHigh;
        public uint LastAccessTimeLow, LastAccessTimeHigh;
        public uint LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }
#pragma warning restore CS0649, CS0169

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);
}
