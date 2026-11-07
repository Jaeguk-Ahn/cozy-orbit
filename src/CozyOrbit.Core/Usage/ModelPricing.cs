namespace CozyOrbit.Core.Usage;

/// <summary>
/// 모델별 가격표 (USD / 1M 토큰). 원본 ModelPricing.swift 의 표를 그대로 옮겼다.
/// 정확히 일치하는 키만 사용 (부분 일치 폴백 없음). 모르는 모델은 "알 수 없음"으로 표시.
/// 장문 컨텍스트 할증 때문에 요청(레코드) 단위로만 계산한다.
/// </summary>
public static class ModelPricing
{
    public sealed record Rate(double Input, double Output, double CacheWrite, double CacheRead);

    private static readonly Dictionary<string, Rate> Table = new(StringComparer.Ordinal)
    {
        ["claude-opus-5"] = new(5, 25, 6.25, 0.5),
        ["claude-opus-5-5"] = new(4, 20, 5, 0.2),
        ["claude-sonnet-5"] = new(2, 10, 2.5, 0.2),
        ["claude-sonnet-5-5"] = new(2, 10, 2.5, 0.1),
        ["claude-opus-4-20250514"] = new(15, 75, 18.75, 1.5),
        ["claude-sonnet-4-20250514"] = new(3, 15, 3.75, 0.3),
        ["claude-sonnet-4-5-20250929"] = new(3, 15, 3.75, 0.3),
        ["claude-opus-4-8"] = new(5, 25, 6.25, 0.5),
        ["claude-opus-4-7"] = new(5, 25, 6.25, 0.5),
        ["claude-sonnet-4-6"] = new(3, 15, 3.75, 0.3),
        ["claude-haiku-4-5-20251001"] = new(1, 5, 1.25, 0.1),
        ["claude-fable-5"] = new(10, 50, 12.5, 1.0),
        ["claude-fable-5-1"] = new(10, 50, 12.5, 0.25),
        ["gpt-6-astra"] = new(10, 50, 12.5, 1),
        ["gpt-6-sol"] = new(2, 10, 2.5, 0.2),
        ["gpt-6.1-sol"] = new(2, 10, 2.5, 0.1),
        ["gpt-6-luna"] = new(0.1, 0.5, 0.125, 0.01),
        ["gpt-5.6-sol"] = new(4, 20, 5, 0.4),
        ["gpt-5.6-terra"] = new(2, 12, 2.5, 0.2),
        ["gpt-5.6-luna"] = new(0.2, 1.2, 0.25, 0.02),
        ["gpt-5"] = new(1.25, 10, 0, 0.125),
        ["gpt-5-codex"] = new(1.25, 10, 0, 0.125),
        ["gpt-5.1"] = new(1.25, 10, 0, 0.125),
        ["gpt-5.1-codex"] = new(1.25, 10, 0, 0.125),
        ["gpt-5.2"] = new(1.75, 14, 0, 0.175),
        ["gpt-5.2-codex"] = new(1.75, 14, 0, 0.175),
        ["gpt-5.3-codex"] = new(1.75, 14, 0, 0.175),
        ["gpt-5.4"] = new(2.5, 15, 0, 0.25),
        ["gpt-5.5"] = new(5, 30, 0, 0.5),
        ["gemini-2.5-pro"] = new(1.25, 10, 0, 0.125),
        ["gemini-2.5-flash"] = new(0.30, 2.5, 0, 0.03),
        ["gemini-2.5-flash-lite"] = new(0.10, 0.40, 0, 0.01),
        ["gemini-2.0-flash"] = new(0.10, 0.4, 0, 0.025),
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["gpt-5.6"] = "gpt-5.6-sol",
        ["claude-sonnet-4"] = "claude-sonnet-4-20250514",
        ["claude-opus-4"] = "claude-opus-4-20250514",
        ["claude-sonnet-4-5"] = "claude-sonnet-4-5-20250929",
        ["claude-haiku-4-5"] = "claude-haiku-4-5-20251001",
        ["gpt-5-2025-08-07"] = "gpt-5",
        ["gpt-5.1-2025-11-13"] = "gpt-5.1",
        ["gpt-5.2-2025-12-11"] = "gpt-5.2",
        ["gpt-5.4-2026-03-05"] = "gpt-5.4",
        ["gpt-5.5-2026-04-23"] = "gpt-5.5",
    };

    private static readonly HashSet<string> LongContext272K = new(StringComparer.Ordinal)
    {
        "gpt-6-astra", "gpt-6-sol", "gpt-6.1-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5", "gpt-5.4",
    };

    private static readonly string[] Prefixes = { "openai/", "anthropic/", "google/", "models/" };

    public static string ModelKey(string model)
    {
        var key = model.Trim().ToLowerInvariant();
        foreach (var p in Prefixes)
            if (key.StartsWith(p, StringComparison.Ordinal)) { key = key[p.Length..]; break; }
        return Aliases.TryGetValue(key, out var a) ? a : key;
    }

    public static double? EstimatedCost(string model, long input, long output, long cacheWrite, long cacheRead)
    {
        var key = ModelKey(model);
        if (!Table.TryGetValue(key, out var r)) return null;
        if (input < 0 || output < 0 || cacheWrite < 0 || cacheRead < 0) return null;
        if (cacheWrite > 0 && r.CacheWrite == 0) return null; // 0 = 별도 쓰기 요율 없음(무료 아님)
        long prompt = input + cacheRead + cacheWrite;
        bool longContext = (LongContext272K.Contains(key) && prompt > 272_000) || (key == "gemini-2.5-pro" && prompt > 200_000);
        double inMul = longContext ? 2.0 : 1.0, outMul = longContext ? 1.5 : 1.0;
        const double M = 1_000_000;
        return (input * r.Input / M + cacheWrite * r.CacheWrite / M + cacheRead * r.CacheRead / M) * inMul
               + output * r.Output / M * outMul;
    }
}
