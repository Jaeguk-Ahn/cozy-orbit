using System.Text.Json;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Grok CLI (xAI "Grok Build") — sessions/&lt;cwd&gt;/&lt;session&gt;/updates.jsonl 의 turn_completed.usage (spec §4.8).
/// 서브에이전트 세션(summary.json 의 session_kind 가 "subagent*")은 부모 턴에 이미 접혀 있으므로 제외.
/// </summary>
public sealed class GrokUsageProvider : IUsageProvider
{
    private const string UpdatesFileName = "updates.jsonl";

    // 파일별 파싱 결과 캐시 (경로·mtime·크기). 서브에이전트 판정은 캐시 밖(파일 선택 단계)에서 매번 한다.
    private readonly FileParseCache<List<UsageEntry>> _cache = new();

    public string Id => "grok";
    public string DisplayName => "Grok";

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        // GROK_HOME 이 있으면 기본 경로를 대체한다 (CLI 와 같은 규칙, 틸드 확장 없음)
        var grokHome = ctx.Env("GROK_HOME");
        var defaults = string.IsNullOrWhiteSpace(grokHome)
            ? new List<string> { Path.Combine(ctx.Home, ".grok", "sessions") }
            : new List<string> { Path.Combine(grokHome.Trim(), "sessions") };
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
            foreach (var file in FileScan.Files(root, FileScan.ExactName(UpdatesFileName), ctx.Since))
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                // 선택 단계 판정: summary.json 은 캐시 키(updates.jsonl)와 다른 파일이라 매 로드마다 재평가
                if (IsSubagentSession(Path.GetDirectoryName(file))) continue;
                live.Add(file);
                all.AddRange(_cache.GetOrParse(file, ParseFile));
            }
        }
        _cache.Retain(live);
        // fork 가 부모 updates 를 복사해도 prompt_id 가 같아 하나로 접힌다 (가장 이른 날짜 유지 후 기간 필터)
        return Dedup.KeepMax(all).Where(e => e.Date >= ctx.Since).ToList();
    }

    /// <summary>summary.json 의 session_kind 가 subagent 계열인가. 없거나 못 읽으면 사용자 세션.</summary>
    private static bool IsSubagentSession(string? sessionDir)
    {
        if (string.IsNullOrEmpty(sessionDir)) return false;
        var text = FileScan.ReadAllText(Path.Combine(sessionDir, "summary.json"));
        if (text == null) return false;
        using var doc = JsonValues.TryParse(text);
        if (doc == null) return false;
        return doc.RootElement.Prop("session_kind") is { ValueKind: JsonValueKind.String } kind
               && (kind.GetString() ?? "").StartsWith("subagent", StringComparison.Ordinal);
    }

    private static List<UsageEntry> ParseFile(string path)
    {
        var list = new List<UsageEntry>();
        foreach (var line in FileScan.ReadLines(path))
        {
            // 대부분이 스트리밍 청크 라인 → JSON 파싱 전에 문자열로 거른다
            if (!line.Contains("turn_completed", StringComparison.Ordinal)) continue;
            UsageEntry? e;
            try { e = ParseLine(line); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { e = null; } // 손상된 숫자 등
            if (e != null) list.Add(e);
        }
        return Dedup.KeepMax(list);
    }

    internal static UsageEntry? ParseLine(string line)
    {
        using var doc = JsonValues.TryParse(line);
        if (doc == null) return null;
        var envelope = doc.RootElement;
        if (envelope.ValueKind != JsonValueKind.Object) return null;

        // 봉투 → 알림 → 업데이트. params 가 없는 구형 라인은 알림 그 자체.
        var notification = envelope.Prop("params") is { ValueKind: JsonValueKind.Object } p ? p : envelope;
        if (notification.Prop("update") is not { ValueKind: JsonValueKind.Object } update) return null;
        if (RawString(update.Prop("sessionUpdate")) != "turn_completed") return null;
        if (update.Prop("usage") is not { ValueKind: JsonValueKind.Object } usage) return null;

        var meta = notification.Prop("_meta");
        if (JsonValues.Bool(meta.Prop("isReplay"))) return null;

        // 턴 id 는 prompt_id 만 (세션 경로를 섞지 않음 → fork 복사본이 하나로 접힘)
        var turnId = JsonValues.NonEmptyString(update.Prop("prompt_id"));
        if (turnId == null) return null;
        if (GrokDate(envelope, meta) is not { } date) return null;

        // null 은 "값 없음" — 표기(camel/snake)는 숫자 존재로 고른다
        long output = JsonValues.IntOrNull(usage.Prop("outputTokens"))
                      ?? JsonValues.IntOrNull(usage.Prop("output_tokens")) ?? 0;
        long reportedCacheRead = JsonValues.IntOrNull(usage.Prop("cachedReadTokens"))
                                 ?? JsonValues.IntOrNull(usage.Prop("cached_read_tokens")) ?? 0;
        long input, cacheRead;
        if (JsonValues.IntOrNull(usage.Prop("inputTokens")) is { } full)
        {
            // camelCase inputTokens 는 캐시 읽기 포함 → 캐시분을 빼되 전체를 넘지 않게 clamp
            cacheRead = Math.Min(reportedCacheRead, full);
            input = full - cacheRead;
        }
        else
        {
            // snake_case input_tokens 는 이미 캐시 제외
            input = JsonValues.IntOrNull(usage.Prop("input_tokens")) ?? 0;
            cacheRead = reportedCacheRead;
        }

        // 보고된 total 보다 부분합이 작으면 차이는 output 으로 (reasoning 은 이미 output 안)
        var reportedTotal = JsonValues.IntOrNull(usage.Prop("totalTokens")) ?? JsonValues.IntOrNull(usage.Prop("total_tokens"));
        long parts = input + output + cacheRead;
        if (reportedTotal is { } rt && rt > parts) output += rt - parts;

        if (input + output + cacheRead <= 0) return null; // 0 토큰 턴(취소 등)

        return UsageEntry.Create("grok|" + turnId, date, Model(usage) ?? "grok",
            input, output, cacheWrite: 0, cacheRead: cacheRead, explicitCost: Cost(usage));
    }

    /// <summary>modelUsage(또는 model_usage) 중 토큰이 가장 많은 키. 동수면 이름 순 첫 번째.</summary>
    private static string? Model(JsonElement usage)
    {
        JsonElement byModel;
        if (usage.Prop("modelUsage") is { ValueKind: JsonValueKind.Object } camel) byModel = camel;
        else if (usage.Prop("model_usage") is { ValueKind: JsonValueKind.Object } snake) byModel = snake;
        else return null;

        string? bestName = null;
        long bestTotal = 0;
        foreach (var prop in byModel.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            long total = JsonValues.IntOrNull(prop.Value.Prop("totalTokens"))
                         ?? JsonValues.IntOrNull(prop.Value.Prop("total_tokens")) ?? 0;
            if (bestName == null || total > bestTotal)
            {
                bestName = prop.Name;
                bestTotal = total;
            }
        }
        var trimmed = bestName?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>서버 계산 비용 (1e10 ticks = $1). 부분합/불완전 플래그가 있으면 버린다.</summary>
    private static double? Cost(JsonElement usage)
    {
        if (JsonValues.Bool(usage.Prop("usageIsIncomplete")) || JsonValues.Bool(usage.Prop("usage_is_incomplete"))) return null;
        if (JsonValues.Bool(usage.Prop("costIsPartial")) || JsonValues.Bool(usage.Prop("cost_is_partial"))) return null;
        var ticks = JsonValues.DoubleOrNull(usage.Prop("costUsdTicks")) ?? JsonValues.DoubleOrNull(usage.Prop("cost_usd_ticks"));
        if (ticks is not { } t || !double.IsFinite(t) || t < 0) return null;
        return t / 1e10;
    }

    /// <summary>_meta.agentTimestampMs 우선 (fork 가 봉투 timestamp 를 새로 찍으므로), 그다음 봉투 timestamp(초/ms/ISO).</summary>
    private static DateTimeOffset? GrokDate(JsonElement envelope, JsonElement? meta)
    {
        var ms = JsonValues.DoubleOrNull(meta.Prop("agentTimestampMs")) ?? 0;
        if (ms > 0) return UsageTime.FromEpochMs(ms);
        var raw = JsonValues.DoubleOrNull(envelope.Prop("timestamp")) ?? 0;
        if (raw > 0) return UsageTime.FromEpochAuto(raw);
        if (envelope.Prop("timestamp") is { ValueKind: JsonValueKind.String } s) return UsageTime.ParseIso(s.GetString());
        return null;
    }

    private static string? RawString(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
}
