using System.Text.Json;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// omp (oh-my-pi) — ~/.omp/agent/sessions/&lt;cwd&gt;/&lt;ts&gt;_&lt;uuid&gt;.jsonl (spec §4.12).
/// assistant 메시지와 compaction/branch_summary 의 usage. 서브에이전트 파일(&lt;id&gt;/__advisor.jsonl)도 센다.
/// 메시지 id 는 세션 안에서만 유일하므로 파일 이름으로 범위를 정한다.
/// </summary>
public sealed class OmpUsageProvider : IUsageProvider
{
    private const string BridgeDirectoryName = "bridge";
    private readonly FileParseCache<List<UsageEntry>> _cache = new();

    public string Id => "omp";
    public string DisplayName => "omp";
    public bool IncludeModels => true;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = new List<string> { Path.Combine(ctx.Home, ".omp", "agent", "sessions") };
        // omp 에는 별도 세션 폴더 변수가 없다 (OMP_CODING_AGENT_DIR 만)
        var agentDir = ctx.Env("OMP_CODING_AGENT_DIR");
        if (!string.IsNullOrWhiteSpace(agentDir))
            defaults.Add(Path.Combine(ExpandTilde(agentDir, ctx.Home), "sessions"));
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
            // bridge/ 는 pi-session-manager 가 다른 세션을 변환한 복사본 → 이중 집계 방지로 제외.
            // 원본은 절대 경로 전체의 구성요소를 검사해 루트 위에 'bridge' 폴더가 있으면 전부 빠지는 문제가 있었다.
            // 여기서는 의도적으로 스캔 루트 *아래* 구성요소만 본다 (skipDirectory 는 루트 자신에게는 호출되지 않음).
            foreach (var file in FileScan.Files(root, FileScan.Extension(".jsonl"), ctx.Since,
                         skipDirectory: dir => string.Equals(Path.GetFileName(dir), BridgeDirectoryName, StringComparison.Ordinal)))
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                live.Add(file);
                all.AddRange(_cache.GetOrParse(file, ParseFile));
            }
        }
        _cache.Retain(live);
        return Dedup.KeepMax(all).Where(e => e.Date >= ctx.Since).ToList();
    }

    private static List<UsageEntry> ParseFile(string path)
    {
        var fileName = Path.GetFileName(path);
        var list = new List<UsageEntry>();
        foreach (var line in FileScan.ReadLines(path))
        {
            // user/toolResult/custom 라인에는 usage 가 없다 → JSON 파싱 전에 거른다
            if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
            UsageEntry? e;
            try { e = ParseLine(line, fileName); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { e = null; }
            if (e != null) list.Add(e);
        }
        return Dedup.KeepMax(list);
    }

    internal static UsageEntry? ParseLine(string line, string fileName)
    {
        using var doc = JsonValues.TryParse(line);
        if (doc == null) return null;
        var envelope = doc.RootElement;
        if (envelope.ValueKind != JsonValueKind.Object) return null;
        var type = RawString(envelope.Prop("type"));
        if (type == null) return null;

        JsonElement usage;
        DateTimeOffset? date;
        string model = "omp";
        switch (type)
        {
            case "message":
            {
                if (envelope.Prop("message") is not { ValueKind: JsonValueKind.Object } message) return null;
                if (RawString(message.Prop("role")) != "assistant") return null;
                var stopReason = RawString(message.Prop("stopReason"));
                if (stopReason == "aborted" || stopReason == "error") return null;
                if (message.Prop("usage") is not { ValueKind: JsonValueKind.Object } messageUsage) return null;
                usage = messageUsage;
                model = RawString(message.Prop("model")) ?? "omp"; // envelope.model 은 쓰지 않는다
                date = MessageDate(message, envelope);
                break;
            }
            case "compaction":
            case "branch_summary":
            {
                if (envelope.Prop("usage") is not { ValueKind: JsonValueKind.Object } envelopeUsage) return null;
                usage = envelopeUsage;
                date = EnvelopeDate(envelope);
                break;
            }
            default:
                return null;
        }
        if (date is not { } d || !HasAnyProperty(usage)) return null;

        // 메시지 id 는 8-hex, 세션 안에서만 유일 → 파일 이름으로 범위 지정
        var id = "omp|" + fileName + "|" + (RawString(envelope.Prop("id")) ?? Guid.NewGuid().ToString("D").ToUpperInvariant());
        // omp 가 기록한 자체 추정 비용. 명시적 0 도 유지.
        var cost = JsonValues.DoubleOrNull(usage.Prop("cost").Prop("total"));
        if (cost is { } c && (!double.IsFinite(c) || c < 0)) cost = null;

        var inputValue = JsonValues.IntOrNull(usage.Prop("input"));
        var outputValue = JsonValues.IntOrNull(usage.Prop("output"));
        var cacheWriteValue = JsonValues.IntOrNull(usage.Prop("cacheWrite"));
        var cacheReadValue = JsonValues.IntOrNull(usage.Prop("cacheRead"));
        if (inputValue != null || outputValue != null || cacheWriteValue != null || cacheReadValue != null)
        {
            return UsageEntry.Create(id, d, model,
                inputValue ?? 0, outputValue ?? 0, cacheWriteValue ?? 0, cacheReadValue ?? 0,
                explicitCost: cost, costIsEstimate: true);
        }
        // total 만 있는 usage: input 으로 보존, 가격표 추정 금지
        if (JsonValues.IntOrNull(usage.Prop("totalTokens")) is not { } total) return null;
        return UsageEntry.Create(id, d, model, total, 0, 0, 0,
            explicitCost: cost, costIsEstimate: true, costUnavailable: true);
    }

    private static bool HasAnyProperty(JsonElement obj)
    {
        foreach (var _ in obj.EnumerateObject()) return true;
        return false;
    }

    private static DateTimeOffset? MessageDate(JsonElement message, JsonElement envelope)
    {
        if (JsonValues.DoubleOrNull(message.Prop("timestamp")) is { } ms && ms > 0) return UsageTime.FromEpochMs(ms);
        return EnvelopeDate(envelope);
    }

    private static DateTimeOffset? EnvelopeDate(JsonElement envelope) =>
        envelope.Prop("timestamp") is { ValueKind: JsonValueKind.String } s ? UsageTime.ParseIso(s.GetString()) : null;

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
