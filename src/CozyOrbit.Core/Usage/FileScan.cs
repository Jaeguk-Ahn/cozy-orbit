using System.Text;

namespace CozyOrbit.Core.Usage;

/// <summary>로그 파일 탐색·읽기 공용 함수.</summary>
public static class FileScan
{
    /// <summary>
    /// 루트 아래를 재귀 탐색해 조건에 맞는 파일을 돌려준다.
    /// 원본(macOS .skipsHiddenFiles)과 같게 '.' 으로 시작하거나 숨김 속성인 항목은 건너뛴다(루트 자신은 예외).
    /// modifiedSince 가 있으면 수정 시각이 그 이후인 파일만.
    /// </summary>
    public static IEnumerable<string> Files(string root, Func<string, bool> fileNameFilter,
        DateTimeOffset? modifiedSince = null, int maxDepth = 64, Func<string, bool>? skipDirectory = null)
    {
        if (!Directory.Exists(root)) yield break;
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (Exception) { continue; } // 권한 없음 등
            foreach (var c in children)
            {
                if (IsHidden(c)) continue;
                if (c is DirectoryInfo d)
                {
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0 && depth > 0) continue; // 링크 순환 방지
                    if (depth + 1 > maxDepth) continue;
                    if (skipDirectory?.Invoke(d.FullName) == true) continue;
                    stack.Push((d.FullName, depth + 1));
                }
                else if (c is FileInfo f)
                {
                    if (!fileNameFilter(f.Name)) continue;
                    if (modifiedSince is { } since && new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero) < since) continue;
                    yield return f.FullName;
                }
            }
        }
    }

    public static bool IsHidden(FileSystemInfo info) =>
        info.Name.StartsWith('.') || (info.Attributes & FileAttributes.Hidden) != 0;

    public static Func<string, bool> Extension(params string[] exts) =>
        name => exts.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static Func<string, bool> ExactName(string fileName) =>
        name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>다른 프로세스가 쓰는 중인 로그도 읽을 수 있게 공유 모드로 연다.</summary>
    public static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    /// <summary>UTF-8 줄 단위 읽기 (BOM 무시, 잘못된 바이트는 대체 문자).</summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        FileStream fs;
        try { fs = OpenShared(path); }
        catch (Exception) { yield break; }
        using (fs)
        using (var reader = new StreamReader(fs, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            string? line;
            while (true)
            {
                try { line = reader.ReadLine(); }
                catch (IOException) { yield break; }
                if (line == null) yield break;
                if (line.Length == 0) continue;
                yield return line;
            }
        }
    }

    public static string? ReadAllText(string path)
    {
        try
        {
            using var fs = OpenShared(path);
            using var reader = new StreamReader(fs, new UTF8Encoding(false), true);
            return reader.ReadToEnd();
        }
        catch (Exception) { return null; }
    }

    public static (DateTime MtimeUtc, long Size)? Signature(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : null;
        }
        catch { return null; }
    }

    /// <summary>SQLite(WAL) 변경 감지: db + -wal 의 최신 mtime 과 크기 합. -shm 은 읽기 전용 접근에도 바뀌므로 제외.</summary>
    public static (DateTime MtimeUtc, long Size)? SqliteSignature(string dbPath)
    {
        var db = Signature(dbPath);
        if (db == null) return null;
        var wal = Signature(dbPath + "-wal");
        if (wal == null) return db;
        return (wal.Value.MtimeUtc > db.Value.MtimeUtc ? wal.Value.MtimeUtc : db.Value.MtimeUtc, db.Value.Size + wal.Value.Size);
    }
}

/// <summary>
/// 파일별 파싱 결과 캐시. (경로, mtime, 크기)가 같으면 다시 파싱하지 않는다.
/// Claude 로그처럼 수 GB 가 될 수 있는 로그를 새로고침마다 다시 읽지 않기 위함.
/// </summary>
public sealed class FileParseCache<T>
{
    private readonly Dictionary<string, (DateTime Mtime, long Size, T Value)> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public T GetOrParse(string path, Func<string, T> parse)
    {
        var sig = FileScan.Signature(path);
        lock (_gate)
        {
            if (sig != null && _map.TryGetValue(path, out var c) && c.Mtime == sig.Value.MtimeUtc && c.Size == sig.Value.Size)
                return c.Value;
        }
        var value = parse(path);
        if (sig != null)
            lock (_gate) _map[path] = (sig.Value.MtimeUtc, sig.Value.Size, value);
        return value;
    }

    /// <summary>이번 스캔에서 보이지 않은 파일 항목 제거 (메모리 누수 방지)</summary>
    public void Retain(IEnumerable<string> livePaths)
    {
        var live = new HashSet<string>(livePaths, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var k in _map.Keys.Where(k => !live.Contains(k)).ToList()) _map.Remove(k);
    }

    public void Clear() { lock (_gate) _map.Clear(); }
}
