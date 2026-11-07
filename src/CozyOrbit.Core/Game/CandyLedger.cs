using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Game;

/// <summary>게임을 켜 둔 구간 하나. 진행 중인 구간은 End 가 계속 늘어난다.</summary>
public sealed class PlaySession
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    /// <summary>이 구간의 사용량 중 이미 별사탕으로 바꿔 준 가중 토큰</summary>
    public double CreditedWeighted { get; set; }
}

/// <summary>별사탕 지갑 + 켜 둔 구간 기록 (세이브에 들어감)</summary>
public sealed class CandyState
{
    /// <summary>가진 별사탕 (소수는 다음 사용량과 합쳐진다 — 화면에는 내림)</summary>
    public double Balance { get; set; }
    /// <summary>지금까지 받은 별사탕</summary>
    public double Earned { get; set; }
    /// <summary>최근 켜 둔 구간 (오래된 것부터, 마지막이 지금 구간)</summary>
    public List<PlaySession> Sessions { get; set; } = new();
}

/// <summary>
/// 켜 둔 동안 쓴 AI 토큰 → 별사탕 (docs/design.md §9). 별빛과 달리 곡선 없이 가중 토큰에 비례한다.
/// 구간마다 "지급한 가중 토큰"만 기억하는 단조 장부라 같은 사용량을 두 번 주지 않고, 읽기 실패로 줄어도 빼앗지 않는다.
/// 게임이 꺼져 있던 동안(또는 컴퓨터가 잠들어 진행이 멈췄던 동안)의 사용량은 별사탕이 되지 않는다.
/// </summary>
public static class CandyLedger
{
    /// <summary>별사탕 1개 = 가중 토큰 5,000</summary>
    public const double WeightedPerCandy = 5000;

    /// <summary>새 구간을 연다 (실행 직후, 오래 멈췄다 깨어났을 때). 일주일 넘게 지난 구간은 지운다 — 스캔 하한 밖이다.</summary>
    public static void BeginSession(CandyState c, DateTimeOffset now)
    {
        var floor = now - Balance.MaxScanBack;
        c.Sessions.RemoveAll(x => x.End < floor);
        c.Sessions.Add(new PlaySession { Start = now, End = now });
    }

    /// <summary>지금 구간의 끝을 now 로 늘린다</summary>
    public static void Touch(CandyState c, DateTimeOffset now)
    {
        if (c.Sessions.Count == 0) BeginSession(c, now);
        var current = c.Sessions[^1];
        if (now > current.End) current.End = now;
    }

    /// <summary>스캔 결과를 구간별로 맞춰 새로 생긴 몫만큼 별사탕을 준다. 돌려주는 값 = 이번에 받은 별사탕.</summary>
    public static double Apply(CandyState c, IReadOnlyList<ProviderScan> scans, DateTimeOffset now)
    {
        Touch(c, now);
        double gained = 0;
        foreach (var session in c.Sessions)
        {
            double w = 0;
            foreach (var scan in scans)
                foreach (var e in scan.Entries)
                    if (e.Date >= session.Start && e.Date <= session.End) w += StarlightLedger.Weighted(e);
            if (w <= session.CreditedWeighted) continue;
            gained += (w - session.CreditedWeighted) / WeightedPerCandy;
            session.CreditedWeighted = w;
        }
        c.Balance += gained;
        c.Earned += gained;
        return gained;
    }
}
