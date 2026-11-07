using System.Globalization;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Game;

/// <summary>세이브에 들어가는 별빛 지급 장부</summary>
public sealed class StarlightLedgerState
{
    /// <summary>이 시각 이전의 사용량은 세지 않는다 (새 게임의 오늘 0시)</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>적어도 한 소스를 읽은 마지막 스캔 시각</summary>
    public DateTimeOffset? LastScanAt { get; set; }
    /// <summary>로컬 날짜 "yyyy-MM-dd" → 이미 별빛으로 바꿔 준 가중 토큰</summary>
    public Dictionary<string, double> CreditedWeighted { get; set; } = new();
}

/// <summary>오늘 한 소스의 사용량 (기록 화면용)</summary>
public sealed record ProviderToday(string ProviderId, string DisplayName, long Tokens, double Weighted, string? Error);

/// <summary>한 번의 지급 결과</summary>
public sealed class CreditResult
{
    /// <summary>배터리에 실제로 담긴 별빛</summary>
    public double StarlightAdded { get; init; }
    /// <summary>배터리가 가득 차 별가루로 바뀐 별빛</summary>
    public double OverflowStarlight { get; init; }
    public double OverflowDust { get; init; }
    public required IReadOnlyList<ProviderToday> Today { get; init; }

    public double TotalStarlight => StarlightAdded + OverflowStarlight;
}

/// <summary>
/// 토큰 → 별빛 지급 (docs/design.md §3). 날짜별 "지급한 가중 토큰"만 기억하는 단조 장부라 같은 사용량을 두 번 주지 않고,
/// 소스 읽기 실패로 합계가 줄어도 빼앗지 않는다 (복구되면 차이만큼 따라잡는다).
/// </summary>
public static class StarlightLedger
{
    public static double Weighted(UsageEntry e) =>
        e.Output * Balance.WeightOutput
        + e.Input * Balance.WeightInput
        + e.CacheWrite * Balance.WeightCacheWrite
        + e.CacheRead * Balance.WeightCacheRead;

    /// <summary>스캔 하한 = max(시작 시각, 마지막 성공 스캔 날의 0시, now − 7일)</summary>
    public static DateTimeOffset ScanSince(StarlightLedgerState ledger, DateTimeOffset now)
    {
        var since = ledger.StartedAt;
        if (ledger.LastScanAt is { } last)
        {
            var lastDay = GameState.LocalMidnight(last);
            if (lastDay > since) since = lastDay;
        }
        var floor = now - Balance.MaxScanBack;
        return since > floor ? since : floor;
    }

    public static string DayKey(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>오늘 이미 받은 별빛 (곡선 위의 위치)</summary>
    public static double StarlightToday(StarlightLedgerState ledger, DateTimeOffset now) =>
        Balance.DailyStarlight(ledger.CreditedWeighted.TryGetValue(DayKey(now), out var w) ? w : 0);

    public static CreditResult Apply(GameState s, IReadOnlyList<ProviderScan> scans, DateTimeOffset now)
    {
        var ledger = s.Ledger;
        var todayKey = DayKey(now);
        var perDay = new Dictionary<string, double>();
        var today = new List<ProviderToday>();

        foreach (var scan in scans)
        {
            long todayTokens = 0;
            double todayWeighted = 0;
            foreach (var e in scan.Entries)
            {
                if (e.Date < ledger.StartedAt) continue;
                double w = Weighted(e);
                perDay[e.LocalDay] = perDay.GetValueOrDefault(e.LocalDay) + w;
                if (e.LocalDay == todayKey)
                {
                    todayTokens += e.Total;
                    todayWeighted += w;
                }
            }
            if (todayTokens > 0 || scan.Error != null)
                today.Add(new ProviderToday(scan.ProviderId, scan.DisplayName, todayTokens, todayWeighted, scan.Error));
        }

        double credit = 0;
        foreach (var (day, w) in perDay)
        {
            double before = ledger.CreditedWeighted.GetValueOrDefault(day);
            if (w <= before) continue;
            credit += Balance.DailyStarlight(w) - Balance.DailyStarlight(before);
            s.Stats.WeightedTokens += w - before;
            ledger.CreditedWeighted[day] = w;
        }

        var keepFrom = DayKey(now.AddDays(-Balance.LedgerKeepDays));
        foreach (var day in ledger.CreditedWeighted.Keys.Where(d => string.CompareOrdinal(d, keepFrom) < 0).ToList())
            ledger.CreditedWeighted.Remove(day);

        if (scans.Any(x => x.Error == null)) ledger.LastScanAt = now;

        double room = Math.Max(0, GameRules.BatteryCapacity(s) - s.Starlight);
        double added = Math.Min(room, credit);
        double overflow = credit - added;
        double overflowDust = overflow * Balance.OverflowDustPerStarlight;
        s.Starlight += added;
        GameRules.AddDust(s, overflowDust);
        s.Stats.StarlightEarned += credit;
        s.Stats.StarlightOverflow += overflow;

        return new CreditResult
        {
            StarlightAdded = added,
            OverflowStarlight = overflow,
            OverflowDust = overflowDust,
            Today = today.OrderByDescending(t => t.Weighted).ToList(),
        };
    }
}
