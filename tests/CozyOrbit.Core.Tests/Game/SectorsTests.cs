using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class SectorsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static GameState NewGame()
    {
        var s = GameState.New(T0);
        s.Settings.AutoBoost = false;
        return s;
    }

    /// <summary>우리 은하 첫 구역의 천체 수</summary>
    private static int FirstSectorSize => Sectors.Size(Destinations.MilkyWay, 1);

    [Fact]
    public void IncompleteSectorPaysNothing()
    {
        var s = NewGame();
        s.DiscoveryCount = FirstSectorSize - 1;
        Assert.Empty(Sectors.ClaimCompleted(s));
        Assert.Equal(0, s.Stardust);
        Assert.Equal(0, s.Candy.Balance);
        Assert.Empty(s.SectorsClaimed);
        Assert.Equal(0, s.Stats.SectorsCompleted);
    }

    [Fact]
    public void CompletingASectorPaysExactlyOnce()
    {
        var s = NewGame();
        s.DiscoveryCount = FirstSectorSize;
        double expected = Sectors.StardustReward(s, 1);

        var paid = Assert.Single(Sectors.ClaimCompleted(s));
        Assert.Equal(1, paid.Sector.Number);
        Assert.Equal(expected, paid.Stardust);
        Assert.Equal(expected, s.Stardust);
        Assert.Equal(expected, s.Stats.StardustEarned);
        Assert.Equal(Balance.SectorCandy, s.Candy.Balance);
        Assert.Equal(Balance.SectorCandy, s.Candy.Earned);
        Assert.Equal(1, s.Stats.SectorsCompleted);
        Assert.True(Sectors.IsClaimed(s, 1));

        // 다음 발견이 와도 같은 구역은 다시 주지 않는다
        s.DiscoveryCount++;
        Assert.Empty(Sectors.ClaimCompleted(s));
        Assert.Equal(expected, s.Stardust);
        Assert.Equal(1, s.Stats.SectorsCompleted);
    }

    [Fact]
    public void RewardIsAtLeastThreeTimesTheLastDiscoveryAndGrowsWithIncome()
    {
        var s = NewGame();
        int end = Sectors.EndCount(Destinations.MilkyWay, 4);
        Assert.Equal(Balance.DiscoveryReward(0, end - 1) * Balance.SectorRewardDiscoveries, Sectors.StardustReward(s, 4));

        // 수입이 커진 후반엔 부스트 없는 수입 2시간치
        s.Upgrades[Upgrades.Engine] = 40;
        s.Upgrades[Upgrades.Scope] = 40;
        double perSecond = GameRules.Speed(s) * Balance.DustPerStep * GameRules.DustMultiplier(s);
        Assert.Equal(Math.Round(perSecond * Balance.SectorRewardSeconds), Sectors.StardustReward(s, 4));

        // 부스트 여부로 흔들리지 않는다
        double before = Sectors.StardustReward(s, 4);
        s.Starlight = 1000;
        s.ManualBoost = true;
        Assert.Equal(before, Sectors.StardustReward(s, 4));
    }

    [Fact]
    public void SeveralCompletedSectorsArePaidTogether()
    {
        // 이 기능 이전 세이브를 처음 열 때: 이미 채운 구역을 한꺼번에
        var s = NewGame();
        s.DiscoveryCount = Sectors.EndCount(Destinations.MilkyWay, 3) + 2;
        var paid = Sectors.ClaimCompleted(s);
        Assert.Equal(new[] { 1, 2, 3 }, paid.Select(p => p.Sector.Number));
        Assert.Equal(3, s.Stats.SectorsCompleted);
        Assert.Equal(paid.Sum(p => p.Stardust), s.Stardust);
    }

    [Fact]
    public void NewVoyageStartsItsOwnSectors()
    {
        var s = NewGame();
        s.DiscoveryCount = Destinations.MilkyWay.Catalog.Count;
        Assert.Equal(Destinations.MilkyWay.Sectors.Count, Sectors.ClaimCompleted(s).Count);
        Assert.True(Voyages.Depart(s));

        // 안드로메다: 아직 아무 구역도 완성 전
        Assert.False(Sectors.IsClaimed(s, 1));
        Assert.Empty(Sectors.ClaimCompleted(s));

        s.DiscoveryCount = Sectors.Size(Destinations.Andromeda, 1);
        var paid = Assert.Single(Sectors.ClaimCompleted(s));
        Assert.Same(Destinations.Andromeda, paid.Destination);
        Assert.Equal(1, paid.Sector.Number);
        Assert.Equal(Destinations.MilkyWay.Sectors.Count + 1, s.Stats.SectorsCompleted);
        // 새 항해의 큰 배율이 보상에도 붙는다
        Assert.True(paid.Stardust >= Balance.DiscoveryReward(1, 0));
    }

    [Fact]
    public void SectorsOfVoyagesFinishedBeforeTheFeatureCountWithoutPaying()
    {
        // 구역 보상이 생기기 전에 우리 은하를 끝내고 안드로메다에 와 있는 세이브
        var s = NewGame();
        s.Voyage = 1;
        s.DiscoveryCount = 0;
        double dust = s.Stardust;
        Sectors.BackfillCompletedCount(s);
        Assert.Equal(Destinations.MilkyWay.Sectors.Count, s.Stats.SectorsCompleted);
        Assert.Equal(dust, s.Stardust);

        // 다시 불러도 그대로, 이번 항해에서 받은 구역은 이어서 센다
        s.DiscoveryCount = Sectors.Size(Destinations.Andromeda, 1);
        Sectors.ClaimCompleted(s);
        Sectors.BackfillCompletedCount(s);
        Assert.Equal(Destinations.MilkyWay.Sectors.Count + 1, s.Stats.SectorsCompleted);
    }

    [Fact]
    public void EndlessCosmosHasNoSectorRewards()
    {
        var s = NewGame();
        s.Voyage = Destinations.All.Count;
        s.DiscoveryCount = 50;
        Assert.Empty(Sectors.ClaimCompleted(s));
    }

    [Fact]
    public void FoundThisVoyageCountsOnlyThisVoyagesDiscoveries()
    {
        var s = NewGame();
        s.DiscoveryCount = FirstSectorSize + 2;
        Assert.Equal(FirstSectorSize, Sectors.FoundThisVoyage(s, 1));
        Assert.Equal(2, Sectors.FoundThisVoyage(s, 2));
        Assert.Equal(0, Sectors.FoundThisVoyage(s, 3));
    }

    [Fact]
    public void OldSavesWithoutTheFieldLoadEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cozy-sectors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "save.json");
            File.WriteAllText(path, "{\"version\":" + GameState.CurrentVersion + ",\"discoveryCount\":7,\"stats\":{\"pets\":3}}");
            var s = new SaveStore(path).Load(T0);
            Assert.Empty(s.SectorsClaimed);
            Assert.Equal(0, s.Stats.WhaleGreets);
            Assert.Equal(0, s.Stats.PlayStreak);
            Assert.Null(s.Stats.LastPlayDay);
            Assert.Equal(3, s.Stats.Pets);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
