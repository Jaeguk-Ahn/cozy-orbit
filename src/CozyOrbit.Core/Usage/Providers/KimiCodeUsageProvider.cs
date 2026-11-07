using System.Text.Json;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Kimi Code — sessions/&lt;workDirKey&gt;/&lt;sessionId&gt;/agents/&lt;agent&gt;/wire.jsonl 의 usage.record (spec §4.14).
/// 메인과 모든 서브에이전트가 각자 요청을 기록하므로 wire.jsonl 은 전부 센다.
/// </summary>
public sealed class KimiCodeUsageProvider : IUsageProvider
{
    private const string WireFileName = "wire.jsonl";
    private readonly FileParseCache<List<UsageEntry>> _cache = new();

    public string Id => "kimi_code";
    public string DisplayName => "Kimi Code";
    public bool IncludeModels => true;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = new List<string>
        {
            Path.Combine(ctx.Home, ".kimi-code", "sessions"),
            // 데스크톱 앱 내장 런타임 (macOS ~/Library/Application Support 대응 — Windows 경로는 추정)
            Path.Combine(ctx.RoamingAppData, "kimi-desktop", "daimon-share", "daimon", "runtime", "kimi-code", "home", "sessions"),
        };
        var kimiHome = ctx.Env("KIMI_CODE_HOME");
        if (!string.IsNullOrWhiteSpace(kimiHome))
            defaults.Add(Path.Combine(ExpandTilde(kimiHome, ctx.Home), "sessions"));
        return ScanRoots.Union(defaults, ctx.CustomRootsRaw);
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run(() => Load(ctx), ctx.CancellationToken);

    private IReadOnlyList<UsageEntry> Load(ProviderContext ctx)
    {
        var all = new List<UsageEntry>();
        var live = new List<string>();
        foreach (var root in ResolveRoots(ctx))
        {
            foreach (var file in FileScan.Files(root, FileScan.ExactName(WireFileName), ctx.Since))
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                live.Add(file);
                all.AddRange(_cache.GetOrParse(file, ParseFile));
            }
        }
        _cache.Retain(live);
        // 내용 기반 id 라 /fork 복사본이 하나로 접힌다
        return Dedup.KeepMax(all).Where(e => e.Date >= ctx.Since).ToList();
    }

    private static List<UsageEntry> ParseFile(string path)
    {
        var list = new List<UsageEntry>();
        foreach (var line in FileScan.ReadLines(path))
        {
            // 대부분은 메시지/요청 추적 라인 → JSON 파싱 전에 거른다
            if (!line.Contains("\"usage.record\"", StringComparison.Ordinal)) continue;
            UsageEntry? e;
            try { e = ParseLine(line); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { e = null; }
            if (e != null) list.Add(e);
        }
        return list; // 원본도 파일 단위 dedup 없음 (전역 dedup 만)
    }

    internal static UsageEntry? ParseLine(string line)
    {
        using var doc = JsonValues.TryParse(line);
        if (doc == null) return null;
        var record = doc.RootElement;
        if (record.ValueKind != JsonValueKind.Object) return null;
        if (RawString(record.Prop("type")) != "usage.record") return null;
        if (record.Prop("usage") is not { ValueKind: JsonValueKind.Object } usage) return null;
        if (JsonValues.DoubleOrNull(record.Prop("time")) is not { } millis || millis <= 0) return null;

        // turn / session 은 모두 요청 단위 델타. 모르는 scope 는 추측하지 않고 건너뛴다.
        var scope = RawString(record.Prop("usageScope"));
        if (scope != null && scope != "turn" && scope != "session") return null;

        long input = JsonValues.IntOrNull(usage.Prop("inputOther")) ?? 0;
        long output = JsonValues.IntOrNull(usage.Prop("output")) ?? 0;
        long cacheRead = JsonValues.IntOrNull(usage.Prop("inputCacheRead")) ?? 0;
        long cacheWrite = JsonValues.IntOrNull(usage.Prop("inputCacheCreation")) ?? 0;
        var rawModel = RawString(record.Prop("model"));
        var model = string.IsNullOrEmpty(rawModel) ? "kimi-code" : rawModel;

        if (UsageTime.FromEpochMs(millis) is not { } date) return null;
        // Int64(millis): 0 쪽으로 자름, 범위 초과는 포화
        long ms = millis >= 9.223372036854776e18 ? long.MaxValue : (long)millis;
        // 레코드에 id 가 없다 — /fork 가 기록을 복사하므로 경로 대신 내용으로 식별
        var id = FormattableString.Invariant(
            $"kimi|{ms}|{model}|{scope ?? "-"}|{input}|{output}|{cacheRead}|{cacheWrite}");
        return UsageEntry.Create(id, date, model, input, output, cacheWrite, cacheRead);
    }

    private static string? RawString(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static string ExpandTilde(string path, string home)
    {
        var p = path.Trim();
        if (p == "~") return home;
        if (p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(home, p[2..]);
        return p;
    }
}
