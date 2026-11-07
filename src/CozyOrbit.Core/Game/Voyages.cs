namespace CozyOrbit.Core.Game;

/// <summary>
/// 새 항해 (환생, docs/design.md §11). 목적지 도감을 다 채우면 연구소를 다음 목적지로 옮긴다:
/// 거리·발견 수·별가루·연구 레벨은 처음부터, 항해 배율(속도·별가루 ×1.6)이 영구로 쌓이고 별사탕을 받는다.
/// 남는 것: 별빛, 별사탕, 스킨·소품, 도감, 기록, 목표·도전과제.
/// </summary>
public static class Voyages
{
    public static bool CanDepart(GameState s) => s.DiscoveryCount >= Destinations.GoalCount(Destinations.Current(s));

    /// <summary>떠나면 받을 별사탕</summary>
    public static double DepartureCandy(GameState s) => Balance.VoyageCandyBase * (s.Voyage + 1);

    /// <summary>떠난 뒤의 항해 배율</summary>
    public static double NextMultiplier(GameState s) => Math.Pow(Balance.VoyageBonus, s.Voyage + 1);

    public static bool Depart(GameState s)
    {
        if (!CanDepart(s)) return false;
        double candy = DepartureCandy(s);
        s.Candy.Balance += candy;
        s.Candy.Earned += candy;
        s.Voyage++;
        s.Distance = 0;
        s.DiscoveryCount = 0;
        s.Stardust = 0;
        s.Upgrades.Clear();
        s.ManualBoost = false;
        return true;
    }
}
