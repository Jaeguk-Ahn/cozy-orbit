using System.Text.Json;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Pi Agent — ~/.pi/agent/sessions/**/*.jsonl (spec §4.11).
/// message / compaction / branch_summary 의 usage. 엔트리 id 는 envelope.id (전역: fork 복사본이 하나로 접힘).
/// </summary>
public sealed class PiUsageProvider : IUsageProvider
{
    private readonly FileParseCache<List<UsageEntry>> _cache = new();

    public string Id => "pi";
    public string DisplayName => "Pi";
    public bool IncludeModels => true;

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = new List<string> { Path.Combine(ctx.Home, ".pi", "agent", "sessions") };
        var agentDir = ctx.Env("PI_CODING_AGENT_DIR");
        if (!string.IsNullOrWhiteSpace(agentDir))
            defaults.Add(Path.Combine(ExpandTilde(agentDir, ctx.Home), "sessions"));
        var sessionDir = ctx.Env("PI_CODING_AGENT_SESSION_DIR");
        if (!string.IsNullOrWhiteSpace(sessionDir))
            defaults.Add(ExpandTilde(sessionDir, ctx.Home));
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
            foreach (var file in FileScan.Files(root, FileScan.Extension(".jsonl"), ctx.Since))
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
        var list = new List<UsageEntry>();
        foreach (var line in FileScan.ReadLines(path))
        {
            if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
            UsageEntry? e;
            try { e = ParseLine(line); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { e = null; }
            if (e != null) list.Add(e);
        }
        return Dedup.KeepMax(list); // 파일 내 dedup (전역 dedup 은 Load 에서)
    }

    internal static UsageEntry? ParseLine(string line)
    {
        using var doc = JsonValues.TryParse(line);
        if (doc == null) return null;
        var envelope = doc.RootElement;
        if (envelope.ValueKind != JsonValueKind.Object) return null;
        var id = RawString(envelope.Prop("id"));
        if (string.IsNullOrEmpty(id)) return null;
        var type = RawString(envelope.Prop("type"));
        if (type == null) return null;

        JsonElement usage;
        DateTimeOffset? date;
        string model = "pi";
        switch (type)
        {
            case "message":
            {
                // role 필터 없음 (usage 가 있는 toolResult 메시지도 센다)
                if (envelope.Prop("message") is not { ValueKind: JsonValueKind.Object } message) return null;
                var stopReason = RawString(message.Prop("stopReason"));
                if (stopReason == "aborted" || stopReason == "error") return null;
                if (message.Prop("usage") is not { ValueKind: JsonValueKind.Object } messageUsage) return null;
                usage = messageUsage;
                date = MessageDate(message, envelope);
                // OMP 계열은 envelope 에, 순정 Pi 는 message 안에 모델을 쓴다
                model = RawString(envelope.Prop("model")) ?? RawString(message.Prop("model")) ?? "pi";
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
        if (date is not { } d) return null;

        var inputValue = JsonValues.IntOrNull(usage.Prop("input"));
        var outputValue = JsonValues.IntOrNull(usage.Prop("output"));
        var cacheWriteValue = JsonValues.IntOrNull(usage.Prop("cacheWrite"));
        var cacheReadValue = JsonValues.IntOrNull(usage.Prop("cacheRead"));
        bool granular = inputValue != null || outputValue != null || cacheWriteValue != null || cacheReadValue != null;
        long input, output, cacheWrite, cacheRead;
        if (granular)
        {
            input = inputValue ?? 0;
            output = outputValue ?? 0; // reasoning 은 output 의 부분집합 — 더하지 않는다
            cacheWrite = cacheWriteValue ?? 0;
            cacheRead = cacheReadValue ?? 0;
        }
        else if (JsonValues.IntOrNull(usage.Prop("totalTokens")) is { } total)
        {
            // total 만 있는 손상된 usage: 버킷 분리를 알 수 없으니 input 으로 보존 (비용 추정 불가)
            input = total;
            output = 0;
            cacheWrite = 0;
            cacheRead = 0;
        }
        else
        {
            return null;
        }

        var cost = JsonValues.DoubleOrNull(usage.Prop("cost").Prop("total"));
        return UsageEntry.Create(id, d, model, input, output, cacheWrite, cacheRead,
            explicitCost: cost, costIsEstimate: true, costUnavailable: !granular);
    }

    /// <summary>message.timestamp (ms) 우선, 없으면 envelope ISO timestamp</summary>
    private static DateTimeOffset? MessageDate(JsonElement message, JsonElement envelope)
    {
        if (JsonValues.DoubleOrNull(message.Prop("timestamp")) is { } ms && ms > 0) return UsageTime.FromEpochMs(ms);
        return EnvelopeDate(envelope);
    }

    private static DateTimeOffset? EnvelopeDate(JsonElement envelope) =>
        envelope.Prop("timestamp") is { ValueKind: JsonValueKind.String } s ? UsageTime.ParseIso(s.GetString()) : null;

    private static string? RawString(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    /// <summary>"~" 를 ctx.Home 으로 (원본 expandingTildeInPath)</summary>
    private static string ExpandTilde(string path, string home)
    {
        var p = path.Trim();
        if (p == "~") return home;
        if (p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(home, p[2..]);
        return p;
    }
}
