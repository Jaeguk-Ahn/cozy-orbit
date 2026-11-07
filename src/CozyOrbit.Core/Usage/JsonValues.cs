using System.Globalization;
using System.Text.Json;

namespace CozyOrbit.Core.Usage;

/// <summary>
/// JSON 값 추출 도우미. 원본에는 두 계열이 있다:
///  - A 계열(LocalUsageReader: Claude/Codex/Gemini/Grok/Pi/omp/Kimi): 숫자만 인정 (문자열 숫자 거부, bool 은 1/0)
///  - B 계열(LocalAdditionalUsageReader: OpenCode/Hermes/Cursor/Copilot/Kiro): 숫자 문자열도 인정
/// 모든 토큰 값은 [0, 1e18] 로 고정한다 (거대한 값이 들어 있는 파일로 매 새로고침 크래시 방지).
/// </summary>
public static class JsonValues
{
    public const long MaxTokenValue = 1_000_000_000_000_000_000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>객체의 속성. 객체가 아니거나, 없거나, JSON null 이면 null.</summary>
    public static JsonElement? Prop(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined ? null : v;
    }

    public static JsonElement? Prop(this JsonElement? e, string name) => e?.Prop(name);

    public static bool IsObject(this JsonElement? e) => e?.ValueKind == JsonValueKind.Object;
    public static bool IsObject(this JsonElement e) => e.ValueKind == JsonValueKind.Object;

    // ---------- A 계열 ----------

    public static double? DoubleOrNull(JsonElement? e)
    {
        if (e is not { } v) return null;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                // 1e400 처럼 double 범위를 넘는 값은 TryGetDouble 이 false → 아주 큰 양수/음수로 간주
                if (v.TryGetDouble(out var d)) return double.IsFinite(d) ? d : null;
                return v.GetRawText().TrimStart().StartsWith('-') ? double.MinValue : double.MaxValue;
            case JsonValueKind.True: return 1;
            case JsonValueKind.False: return 0;
            default: return null;
        }
    }

    public static long? IntOrNull(JsonElement? e)
    {
        var d = DoubleOrNull(e);
        if (d == null) return null;
        return ClampToken(d.Value);
    }

    public static long Int(JsonElement? e) => IntOrNull(e) ?? 0;

    public static bool Bool(JsonElement? e) => e is { } v && (v.ValueKind == JsonValueKind.True ||
        (v.ValueKind == JsonValueKind.Number && DoubleOrNull(v) is { } bd && bd != 0));

    public static bool IsNumber(JsonElement? e) => e?.ValueKind == JsonValueKind.Number;

    /// <summary>공백 제거 후 비어 있지 않은 문자열</summary>
    public static string? NonEmptyString(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.String } v) return null;
        var s = v.GetString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    // ---------- B 계열 ----------

    public static long IntB(JsonElement? e)
    {
        if (e is not { } v) return 0;
        if (v.ValueKind == JsonValueKind.Number) return DoubleOrNull(v) is { } nd ? ClampToken(nd) : 0;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString()?.Trim();
            if (s != null && long.TryParse(s, NumberStyles.Integer, Inv, out var l)) return Math.Clamp(l, 0, MaxTokenValue);
            if (s != null && decimal.TryParse(s, NumberStyles.Integer, Inv, out var big)) return big <= 0 ? 0 : MaxTokenValue;
        }
        return 0;
    }

    public static double? DoubleB(JsonElement? e)
    {
        if (e is not { } v) return null;
        if (v.ValueKind == JsonValueKind.Number) return DoubleOrNull(v);
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString()?.Trim(), NumberStyles.Float, Inv, out var p) && double.IsFinite(p)) return p;
        return null;
    }

    /// <summary>epoch 숫자(또는 숫자 문자열). 1e11 이상이면 ms.</summary>
    public static DateTimeOffset? DateB(JsonElement? e)
    {
        var raw = DoubleB(e);
        return raw is { } r ? UsageTime.FromEpochAuto(r) : null;
    }

    /// <summary>문자열이면 ISO, 아니면 epoch</summary>
    public static DateTimeOffset? FlexibleDate(JsonElement? e)
    {
        if (e is { ValueKind: JsonValueKind.String } v)
        {
            var iso = UsageTime.ParseIso(v.GetString());
            if (iso != null) return iso;
        }
        return DateB(e);
    }

    public static long ClampToken(double d)
    {
        if (!double.IsFinite(d) || d <= 0) return 0;
        if (d >= MaxTokenValue) return MaxTokenValue;
        return (long)Math.Truncate(d);
    }

    /// <summary>JSON 한 줄 파싱. 실패하면 null (손상된 줄은 건너뜀).</summary>
    public static JsonDocument? TryParse(string line)
    {
        try { return JsonDocument.Parse(line, new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 256 }); }
        catch (JsonException) { return null; }
    }

    public static JsonDocument? TryParse(ReadOnlyMemory<byte> bytes)
    {
        try { return JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 256 }); }
        catch (JsonException) { return null; }
    }
}
