namespace CozyOrbit.Core.Usage;

/// <summary>
/// 정규화된 사용량 레코드 (원본 LocalUsageReader.Entry).
/// totalTokens 는 항상 input + output + cacheWrite + cacheRead. reasoning 은 별도 버킷이 없다.
/// </summary>
public sealed class UsageEntry
{
    public required string Id { get; set; }
    /// <summary>UTC 시각</summary>
    public required DateTimeOffset Date { get; set; }
    /// <summary>로컬 날짜 "yyyy-MM-dd" (파싱 시점의 로컬 타임존 기준)</summary>
    public required string LocalDay { get; set; }
    public string Model { get; set; } = "unknown";
    public long Input { get; set; }
    public long Output { get; set; }
    public long CacheWrite { get; set; }
    public long CacheRead { get; set; }
    /// <summary>소스가 기록한 USD 비용 (소스 자체 추정일 수도 있음)</summary>
    public double? ExplicitCost { get; set; }
    /// <summary>true 면 기록 비용을 "추정"으로 취급</summary>
    public bool CostIsEstimate { get; set; }
    /// <summary>true 면 가격표로 추정하지 않음</summary>
    public bool CostUnavailable { get; set; }

    public long Total => Input + Output + CacheWrite + CacheRead;

    public static UsageEntry Create(string id, DateTimeOffset date, string model,
        long input, long output, long cacheWrite, long cacheRead,
        double? explicitCost = null, bool costIsEstimate = false, bool costUnavailable = false) => new()
    {
        Id = id,
        Date = date.ToUniversalTime(),
        LocalDay = UsageTime.LocalDay(date),
        Model = model,
        Input = Math.Max(0, input),
        Output = Math.Max(0, output),
        CacheWrite = Math.Max(0, cacheWrite),
        CacheRead = Math.Max(0, cacheRead),
        ExplicitCost = explicitCost,
        CostIsEstimate = costIsEstimate,
        CostUnavailable = costUnavailable,
    };

    /// <summary>
    /// 원본 LocalAdditionalUsageReader.makeEntry: 음수 0 처리, 보고된 total 이 각 버킷 합보다 크면 차이를 output 에 더하고
    /// (그 경우 비용 추정 불가), 0 토큰 레코드는 버린다(null).
    /// </summary>
    public static UsageEntry? Make(string id, DateTimeOffset date, string model,
        long input, long output, long cacheWrite, long cacheRead,
        long total = 0, double? cost = null, bool costUnavailable = false, bool costIsEstimate = false)
    {
        long i = Math.Max(0, input), cw = Math.Max(0, cacheWrite), cr = Math.Max(0, cacheRead), o = Math.Max(0, output);
        long parts = i + o + cw + cr;
        bool unclassified = total > parts;
        if (unclassified) o += total - parts;
        if (i + o + cw + cr == 0) return null;
        return Create(id, date, model, i, o, cw, cr, cost, costIsEstimate, costUnavailable || unclassified);
    }

    public override string ToString() => $"{Id} {Date:o} {Model} in={Input} out={Output} cw={CacheWrite} cr={CacheRead}";
}
