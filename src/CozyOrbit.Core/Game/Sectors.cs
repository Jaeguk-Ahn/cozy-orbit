namespace CozyOrbit.Core.Game;

/// <summary>구역 완성 보상 한 건</summary>
public sealed record SectorReward(DestinationDef Destination, SectorDef Sector, double Stardust, double Candy);

/// <summary>
/// 구역 완성 (docs/design.md §6). 이번 항해에서 한 구역의 천체를 모두 만나면 한 번만 별가루 + 별사탕을 받는다.
/// 별가루 = max(구역 마지막 발견 보상 × 3, 부스트 없는 별가루 수입 2시간치), 별사탕 10 (수치는 Balance).
/// 완성 여부는 이번 항해의 발견 수로 본다 — 도감(FoundAt)은 항해를 넘어 남기 때문.
/// 받은 구역은 GameState.SectorsClaimed 에 "항해:구역" 으로 적어, 새 항해(다음 목적지)의 구역은 처음부터 다시 받는다.
/// </summary>
public static class Sectors
{
    public static string Key(int voyage, int sector) => $"{voyage}:{sector}";

    /// <summary>구역 안의 천체 수</summary>
    public static int Size(DestinationDef d, int sector)
    {
        int n = 0;
        foreach (var c in d.Catalog)
            if (c.Sector == sector) n++;
        return n;
    }

    /// <summary>이 구역을 다 채우는 데 필요한 이번 항해 발견 수 (구역 마지막 천체 번호 + 1). 천체가 없는 구역이면 0.</summary>
    public static int EndCount(DestinationDef d, int sector)
    {
        int end = 0;
        for (int i = 0; i < d.Catalog.Count; i++)
            if (d.Catalog[i].Sector == sector) end = i + 1;
        return end;
    }

    /// <summary>이번 항해에서 이 구역의 천체를 몇 개 만났는지</summary>
    public static int FoundThisVoyage(GameState s, int sector)
    {
        var d = Destinations.Current(s);
        int n = 0, end = Math.Min(s.DiscoveryCount, d.Catalog.Count);
        for (int i = 0; i < end; i++)
            if (d.Catalog[i].Sector == sector) n++;
        return n;
    }

    /// <summary>이번 항해에서 이 구역의 천체를 모두 만났는지</summary>
    public static bool IsComplete(GameState s, int sector)
    {
        int end = EndCount(Destinations.Current(s), sector);
        return end > 0 && s.DiscoveryCount >= end;
    }

    public static bool IsClaimed(GameState s, int sector) => s.SectorsClaimed.Contains(Key(s.Voyage, sector));

    /// <summary>이번 항해에서 이 구역을 완성하면 받을 별가루 (지금 연구 수준 기준)</summary>
    public static double StardustReward(GameState s, int sector)
    {
        int end = EndCount(Destinations.Current(s), sector);
        if (end <= 0) return 0;
        double discovery = Balance.DiscoveryReward(s.Voyage, end - 1) * Balance.SectorRewardDiscoveries;
        double income = BaseDustPerSecond(s) * Balance.SectorRewardSeconds;
        return Math.Round(Math.Max(discovery, income), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 이번 항해에서 다 채웠지만 아직 안 받은 구역의 보상을 모두 받는다 (구역마다 한 번).
    /// 발견 직후에 부르면 그 발견으로 완성된 구역이, 예전 세이브를 처음 열면 이미 채운 구역이 한꺼번에 나온다.
    /// </summary>
    public static IReadOnlyList<SectorReward> ClaimCompleted(GameState s)
    {
        var dest = Destinations.Current(s);
        List<SectorReward>? paid = null;
        foreach (var sector in dest.Sectors)
        {
            if (!IsComplete(s, sector.Number) || !s.SectorsClaimed.Add(Key(s.Voyage, sector.Number))) continue;
            double dust = StardustReward(s, sector.Number);
            GameRules.AddDust(s, dust);
            DailyGoals.AddCandy(s, Balance.SectorCandy);
            s.Stats.SectorsCompleted++;
            (paid ??= new()).Add(new SectorReward(dest, sector, dust, Balance.SectorCandy));
        }
        return paid ?? (IReadOnlyList<SectorReward>)Array.Empty<SectorReward>();
    }

    /// <summary>
    /// 이 기능 전에 끝낸 항해의 구역도 "완성한 구역" 수에 넣는다 (보상 없이 — 도전과제가 막히지 않게).
    /// 끝낸 항해는 도감을 다 채웠으니 모든 구역을 완성한 것. 이미 맞는 세이브에선 아무것도 바뀌지 않는다.
    /// </summary>
    public static void BackfillCompletedCount(GameState s)
    {
        int count = 0;
        for (int v = 0; v < s.Voyage; v++)
        {
            var d = Destinations.For(v);
            if (d.IsEndless) continue;
            foreach (var sector in d.Sectors)
                if (EndCount(d, sector.Number) > 0) count++;
        }
        string prefix = $"{s.Voyage}:";
        foreach (var key in s.SectorsClaimed)
            if (key.StartsWith(prefix, StringComparison.Ordinal)) count++;
        s.Stats.SectorsCompleted = Math.Max(s.Stats.SectorsCompleted, count);
    }

    /// <summary>부스트 없이 가만히 날 때의 초당 별가루 (보상이 부스트 여부로 흔들리지 않게)</summary>
    private static double BaseDustPerSecond(GameState s) =>
        GameRules.Speed(s) * Balance.DustPerStep * GameRules.DustMultiplier(s)
        + GameRules.CrewCount(s) * Balance.DustPerCrewPerSecond * GameRules.VoyageMultiplier(s);
}
