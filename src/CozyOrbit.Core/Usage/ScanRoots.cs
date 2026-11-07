using System.Text.RegularExpressions;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage;

/// <summary>
/// 스캔 루트 정규화와 "추가 스캔 폴더"(설정) 처리. 원본 normalizedRoots / CustomScanRoots 포팅.
/// 추가 폴더는 기본 경로에 더해질 뿐 대체하지 않는다. 기본 경로의 상위 폴더(예: 홈 폴더)를 넣으면
/// 숨김 폴더 건너뛰기 때문에 .claude 등에 도달하지 못해 합계가 0 이 되므로 무시한다.
/// </summary>
public static class ScanRoots
{
    private static string Full(string p)
    {
        try
        {
            var full = Path.GetFullPath(p);
            return full.Length > 3 ? full.TrimEnd('\\', '/') : full;
        }
        catch { return p; }
    }

    private static string Key(string p) => Full(p).ToLowerInvariant().Replace('/', '\\');

    private static bool IsUnder(string childKey, string parentKey) =>
        childKey == parentKey || childKey.StartsWith(parentKey.EndsWith('\\') ? parentKey : parentKey + "\\", StringComparison.Ordinal);

    /// <summary>중복 제거 + 이미 포함된 상위 경로 아래의 하위 경로 제거. 원래 순서 유지.</summary>
    public static List<string> Normalize(IEnumerable<string> roots)
    {
        var unique = new List<(string Path, string Key)>();
        var seen = new HashSet<string>();
        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            var full = Full(r);
            var key = Key(full);
            if (seen.Add(key)) unique.Add((full, key));
        }
        var kept = new List<string>();
        foreach (var (_, key) in unique.OrderBy(u => u.Key.Length))
            if (!kept.Any(k => IsUnder(key, k))) kept.Add(key);
        var keptSet = kept.ToHashSet();
        return unique.Where(u => keptSet.Contains(u.Key)).Select(u => u.Path).ToList();
    }

    /// <summary>설정 문자열(쉼표/줄바꿈 구분, ~·%VAR% 확장, 세그먼트별 * ? [] 와일드카드) → 존재하는 폴더 목록</summary>
    public static List<string> ExpandCustom(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var part in raw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = AppPaths.Expand(part.Trim('"'));
            if (!Path.IsPathFullyQualified(p)) continue;
            foreach (var m in ExpandGlob(p))
                if (Directory.Exists(m)) result.Add(m);
        }
        return result;
    }

    private static IEnumerable<string> ExpandGlob(string path)
    {
        if (path.IndexOfAny(new[] { '*', '?', '[' }) < 0) return new[] { path };
        var root = Path.GetPathRoot(path) ?? "";
        var segments = path[root.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> current = new[] { root };
        foreach (var seg in segments)
        {
            if (seg.IndexOfAny(new[] { '*', '?', '[' }) < 0)
            {
                current = current.Select(c => Path.Combine(c, seg)).ToList();
                continue;
            }
            var regex = GlobToRegex(seg);
            current = current.SelectMany(c =>
            {
                try
                {
                    return Directory.Exists(c)
                        ? Directory.EnumerateDirectories(c).Select(d => Path.GetFileName(d)).Where(n => n != null && regex.IsMatch(n))
                            .OrderBy(n => n, StringComparer.Ordinal).Select(n => Path.Combine(c, n!)).ToList()
                        : new List<string>();
                }
                catch { return new List<string>(); }
            }).ToList();
        }
        return current;
    }

    /// <summary>fnmatch 스타일 세그먼트 → 정규식 (대소문자 무시)</summary>
    internal static Regex GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char ch = glob[i];
            switch (ch)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                case '[':
                    int close = glob.IndexOf(']', i + 1);
                    if (close < 0) { sb.Append("\\["); break; }
                    var cls = glob[(i + 1)..close];
                    if (cls.StartsWith('!')) cls = "^" + cls[1..];
                    sb.Append('[').Append(cls.Replace("\\", "\\\\")).Append(']');
                    i = close;
                    break;
                default: sb.Append(Regex.Escape(ch.ToString())); break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>기본 루트 + 추가 루트 (기본 루트의 상위 폴더는 버림)</summary>
    public static List<string> Union(IEnumerable<string> defaults, string? extraRaw)
    {
        var protectedRoots = Normalize(defaults);
        var protectedKeys = protectedRoots.Select(Key).ToList();
        var added = ExpandCustom(extraRaw).Where(x =>
        {
            var xk = Key(x);
            bool isDriveRoot = xk.Length <= 3;
            return !protectedKeys.Any(d => d != xk && (isDriveRoot || IsUnder(d, xk)));
        });
        return Normalize(protectedRoots.Concat(added));
    }

    /// <summary>환경 변수 값을 쉼표로 나눈 경로 목록 (비어 있으면 null → 기본값 사용)</summary>
    public static List<string>? EnvPaths(Func<string, string?> env, string name, bool expandTilde = false)
    {
        var v = env(name);
        if (string.IsNullOrWhiteSpace(v)) return null;
        var list = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => expandTilde ? AppPaths.Expand(p) : p).ToList();
        return list.Count == 0 ? null : list;
    }
}
