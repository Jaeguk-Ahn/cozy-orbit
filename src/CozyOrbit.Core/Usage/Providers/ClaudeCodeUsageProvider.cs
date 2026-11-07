using System.Text.Json;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Claude Code: %USERPROFILE%\.claude\projects\**\*.jsonl (원본 spec §4.1).
/// - type=="assistant" 이고 message.usage 가 있는 줄만, id = message.id|requestId 로 중복 제거(최대 total, 최초 시각)
/// - 같은 파일의 마지막 "cost-state" 줄(Claude 자체 비용 장부)이 있으면 그 모델 비용을 토큰 비율로 배분(SOURCE)
/// - 하위 에이전트 transcript(subagents/*.jsonl)도 자체 메시지 id 가 있어 함께 집계
/// </summary>
public sealed class ClaudeCodeUsageProvider : IUsageProvider
{
    public const string ProviderId = "claude_code";
    public string Id => ProviderId;
    public string DisplayName => "Claude Code";

    private readonly FileParseCache<List<UsageEntry>> _cache = new();
    private (DateTimeOffset At, List<string> Roots)? _rootsCache;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        if (_rootsCache is { } rc && ctx.Now - rc.At < TimeSpan.FromMinutes(5) && ctx.CustomRootsRaw == _lastCustom)
            return rc.Roots;
        var defaults = new List<string>();
        var configDirs = ScanRoots.EnvPaths(ctx.Env, "CLAUDE_CONFIG_DIR", expandTilde: true) ?? new List<string>();
        defaults.AddRange(configDirs.Select(d => Path.Combine(d, "projects")));
        defaults.AddRange(AdditionalAccountRoots(ctx).Select(d => Path.Combine(d, "projects")));
        defaults.Add(ctx.HomePath(".config", "claude", "projects"));
        defaults.Add(ctx.HomePath(".claude", "projects"));
        defaults.AddRange(DesktopEmbeddedRoots(ctx));
        var roots = ScanRoots.Union(defaults, ctx.CustomRootsRaw);
        _rootsCache = (ctx.Now, roots);
        _lastCustom = ctx.CustomRootsRaw;
        return roots;
    }

    private string? _lastCustom;

    /// <summary>~/.claude-*, ~/.claude_* 중 .claude.json 에 oauthAccount 가 있는(로그인된) 설정 폴더</summary>
    private static IEnumerable<string> AdditionalAccountRoots(ProviderContext ctx)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(ctx.Home, ".claude*").ToList(); }
        catch { yield break; }
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (!(name.StartsWith(".claude-", StringComparison.Ordinal) || name.StartsWith(".claude_", StringComparison.Ordinal))) continue;
            var cfg = Path.Combine(d, ".claude.json");
            if (!File.Exists(cfg)) continue;
            bool loggedIn = false;
            try
            {
                using var doc = JsonDocument.Parse(FileScan.ReadAllText(cfg) ?? "{}");
                loggedIn = doc.RootElement.Prop("oauthAccount")?.ValueKind == JsonValueKind.Object;
            }
            catch { }
            if (loggedIn) yield return d;
        }
    }

    /// <summary>
    /// Claude Desktop 데이터 폴더 후보. 설치 방식마다 다르다:
    ///  - 일반 설치: %APPDATA%\Claude
    ///  - MSIX(스토어) 설치: %LOCALAPPDATA%\Claude-Data\Claude_&lt;패키지ID&gt; (실제 PC 에서 확인, 2026-10)
    ///  - MSIX 가상화 경로: %LOCALAPPDATA%\Packages\Claude_&lt;패키지ID&gt;\LocalCache\Roaming\Claude
    /// </summary>
    internal static IEnumerable<string> DesktopDataDirs(ProviderContext ctx)
    {
        var dirs = new List<string> { Path.Combine(ctx.RoamingAppData, "Claude") };
        try { dirs.AddRange(Directory.EnumerateDirectories(Path.Combine(ctx.LocalAppData, "Claude-Data"), "Claude_*")); }
        catch { }
        try
        {
            dirs.AddRange(Directory.EnumerateDirectories(Path.Combine(ctx.LocalAppData, "Packages"), "Claude_*")
                .Select(p => Path.Combine(p, "LocalCache", "Roaming", "Claude")));
        }
        catch { }
        return dirs;
    }

    /// <summary>Claude Desktop 내장 세션: &lt;데이터 폴더&gt;\{local-agent-mode-sessions,claude-code-sessions}\**\.claude\projects</summary>
    private static IEnumerable<string> DesktopEmbeddedRoots(ProviderContext ctx)
    {
        var found = new List<string>();
        foreach (var root in DesktopDataDirs(ctx).SelectMany(d => new[] { "local-agent-mode-sessions", "claude-code-sessions" },
                     (d, s) => Path.Combine(d, s)))
        {
            if (!Directory.Exists(root)) continue;
            var stack = new Stack<(string Dir, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                IEnumerable<string> subs;
                try { subs = Directory.EnumerateDirectories(dir).ToList(); } catch { continue; }
                foreach (var s in subs)
                {
                    var name = Path.GetFileName(s);
                    if (name == "projects" && Path.GetFileName(dir) == ".claude") { found.Add(s); continue; }
                    if (name is "node_modules" or ".git" or "venv" or ".venv") continue;
                    if (depth + 1 >= 7) continue;
                    stack.Push((s, depth + 1));
                }
            }
        }
        return found;
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) => Task.Run<IReadOnlyList<UsageEntry>>(() =>
    {
        var roots = ResolveRoots(ctx);
        var files = roots.SelectMany(r => FileScan.Files(r, FileScan.Extension(".jsonl"), ctx.Since))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var all = new List<UsageEntry>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            all.AddRange(_cache.GetOrParse(f, ParseFile));
        }
        _cache.Retain(files);
        return Dedup.KeepMax(all).Where(e => e.Date >= ctx.Since).ToList();
    }, ctx.CancellationToken);

    internal static List<UsageEntry> ParseFile(string path)
    {
        var entries = new List<UsageEntry>();
        Dictionary<string, double>? lastLedger = null;
        foreach (var line in FileScan.ReadLines(path))
        {
            bool isLedger = line.Contains("\"cost-state\"", StringComparison.Ordinal);
            if (!isLedger && !(line.Contains("\"usage\"", StringComparison.Ordinal) && line.Contains("\"assistant\"", StringComparison.Ordinal)))
                continue;
            using var doc = JsonValues.TryParse(line);
            if (doc == null) continue;
            var obj = doc.RootElement;
            if (isLedger && JsonValues.NonEmptyString(obj.Prop("type")) == "cost-state")
            {
                var ledger = ParseLedger(obj);
                if (ledger.Count > 0) lastLedger = ledger;
                continue;
            }
            var e = ParseLine(obj);
            if (e != null) entries.Add(e);
        }
        var deduped = Dedup.KeepMax(entries);
        if (lastLedger != null) ApplyReportedCost(deduped, lastLedger);
        return deduped;
    }

    internal static UsageEntry? ParseLine(JsonElement obj)
    {
        if (JsonValues.NonEmptyString(obj.Prop("type")) != "assistant") return null;
        var message = obj.Prop("message");
        if (!message.IsObject()) return null;
        var usage = message.Prop("usage");
        if (!usage.IsObject()) return null;
        var ts = obj.Prop("timestamp");
        if (ts?.ValueKind != JsonValueKind.String) return null;
        var date = UsageTime.ParseIso(ts.Value.GetString());
        if (date == null) return null;

        var model = JsonValues.NonEmptyString(message.Prop("model")) ?? "unknown";
        var msgId = message.Prop("id")?.ValueKind == JsonValueKind.String ? message.Prop("id")!.Value.GetString() : "";
        var reqId = obj.Prop("requestId")?.ValueKind == JsonValueKind.String ? obj.Prop("requestId")!.Value.GetString() : "";
        return UsageEntry.Create(
            id: $"{msgId}|{reqId}",
            date: date.Value,
            model: model,
            input: JsonValues.Int(usage.Prop("input_tokens")),
            output: JsonValues.Int(usage.Prop("output_tokens")),
            cacheWrite: JsonValues.Int(usage.Prop("cache_creation_input_tokens")),
            cacheRead: JsonValues.Int(usage.Prop("cache_read_input_tokens")));
    }

    /// <summary>"claude-opus-5[1m]" → "claude-opus-5"</summary>
    internal static string CostModelKey(string model)
    {
        int i = model.IndexOf('[');
        return i >= 0 ? model[..i] : model;
    }

    private static Dictionary<string, double> ParseLedger(JsonElement obj)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var mu = obj.Prop("modelUsage");
        if (mu?.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in mu.Value.EnumerateObject())
        {
            var cost = JsonValues.DoubleOrNull(p.Value.Prop("costUSD"));
            if (cost is { } c && double.IsFinite(c) && c >= 0)
            {
                var key = CostModelKey(p.Name);
                result[key] = result.GetValueOrDefault(key) + c;
            }
        }
        return result;
    }

    /// <summary>파일의 비용 장부를 모델별 토큰 비율로 레코드에 배분 (마지막 레코드가 나머지를 가져가 합계가 정확히 일치)</summary>
    internal static void ApplyReportedCost(List<UsageEntry> fileEntries, Dictionary<string, double> ledger)
    {
        foreach (var (model, cost) in ledger)
        {
            var targets = fileEntries.Where(e => e.Total > 0 && CostModelKey(e.Model) == model).ToList();
            if (targets.Count == 0) continue;
            double totalTokens = targets.Sum(e => (double)e.Total);
            double assigned = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                double share = i == targets.Count - 1 ? cost - assigned : cost * targets[i].Total / totalTokens;
                assigned += share;
                targets[i].ExplicitCost = Math.Max(0, share);
                targets[i].CostIsEstimate = false;
            }
        }
    }
}
