using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Game;

/// <summary>
/// 날짜별 사용량 기록 (기록 화면 그래프용). 별빛 장부처럼 값은 줄지 않는다 — 소스 읽기 실패나
/// 스캔 하한에 걸려 일부만 읽힌 날이 이미 적힌 값을 지우지 않게.
/// </summary>
public static class UsageHistory
{
    /// <summary>이보다 오래된 날은 지운다</summary>
    public const int KeepDays = 400;

    /// <summary>스캔 한 번의 결과를 기록한다 (별빛·별사탕 장부를 적용한 직후에 부른다). candyGained = 이번에 받은 별사탕.</summary>
    public static void Record(GameState s, IReadOnlyList<ProviderScan> scans, double candyGained, DateTimeOffset now)
    {
        var perDay = new Dictionary<string, (long Tokens, double Weighted)>();
        foreach (var scan in scans)
            foreach (var e in scan.Entries)
            {
                if (e.Date < s.Ledger.StartedAt) continue;
                var (tokens, weighted) = perDay.GetValueOrDefault(e.LocalDay);
                perDay[e.LocalDay] = (tokens + e.Total, weighted + StarlightLedger.Weighted(e));
            }

        foreach (var (day, (tokens, weighted)) in perDay)
        {
            var d = Day(s, day);
            if (tokens > d.Tokens) d.Tokens = tokens;
            if (weighted > d.Weighted) d.Weighted = weighted;
            if (s.Ledger.CreditedWeighted.TryGetValue(day, out var credited))
                d.Starlight = Math.Max(d.Starlight, Balance.DailyStarlight(credited));
        }

        if (candyGained > 0) Day(s, StarlightLedger.DayKey(now)).Candy += candyGained;

        var keepFrom = StarlightLedger.DayKey(now.AddDays(-KeepDays));
        foreach (var day in s.History.Keys.Where(d => string.CompareOrdinal(d, keepFrom) < 0).ToList())
            s.History.Remove(day);
    }

    /// <summary>
    /// 별빛 장부가 아는 날 중 기록에 가중 토큰·별빛이 없는 날을 장부로 채운다 (기록 기능 이전 세이브나 별사탕만 적힌 날의 그래프가
    /// 비지 않게). 토큰 수·별사탕은 장부가 모르니 그대로 둔다 — 이미 값이 있는 날은 건드리지 않는다.
    /// </summary>
    public static void SeedFromLedger(GameState s)
    {
        foreach (var (day, w) in s.Ledger.CreditedWeighted)
        {
            if (!(w > 0)) continue;
            if (!s.History.TryGetValue(day, out var d)) s.History[day] = d = new DayUsage();
            else if (d.Weighted > 0 || d.Starlight > 0) continue;
            d.Weighted = w;
            d.Starlight = Balance.DailyStarlight(w);
        }
    }

    private static DayUsage Day(GameState s, string day)
    {
        if (!s.History.TryGetValue(day, out var d)) s.History[day] = d = new DayUsage();
        return d;
    }
}
