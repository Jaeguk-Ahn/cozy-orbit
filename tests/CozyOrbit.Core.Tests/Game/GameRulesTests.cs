using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class GameRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static GameState NewGame(bool autoBoost = false)
    {
        var s = GameState.New(T0);
        s.Settings.AutoBoost = autoBoost;
        return s;
    }

    [Fact]
    public void BoostMultipliesSpeedAndDrainsOneStarlightPerSecond()
    {
        var s = NewGame(autoBoost: true);
        s.Starlight = 10;
        var r = GameRules.Advance(s, 20);

        // 10초는 부스트 배율, 나머지 10초는 ×1
        Assert.Equal(10 * Balance.BaseBoostMultiplier + 10 * 1, r.DistanceGained, 6);
        Assert.Equal(10, r.BoostSeconds, 6);
        Assert.Equal(0, s.Starlight);
    }

    [Fact]
    public void ManualBoostTurnsOffWhenStarlightRunsOut()
    {
        var s = NewGame();
        s.Starlight = 3;
        GameRules.SetManualBoost(s, true);
        Assert.True(s.ManualBoost);
        GameRules.Advance(s, 5);
        Assert.False(s.ManualBoost);

        GameRules.SetManualBoost(s, true);
        Assert.False(s.ManualBoost); // 별빛 없이는 켜지지 않는다
    }

    [Fact]
    public void NoBoostWithoutAutoOrManual()
    {
        var s = NewGame();
        s.Starlight = 100;
        var r = GameRules.Advance(s, 10);
        Assert.Equal(10, r.DistanceGained, 6);
        Assert.Equal(100, s.Starlight);
    }

    [Fact]
    public void DiscoveriesHappenInOrderAtTheirDistancesAndPay()
    {
        var s = NewGame();
        double target = Balance.DiscoveryDistance(0, 2);
        var r = GameRules.Advance(s, target);

        Assert.Equal(new[] { 0, 1, 2 }, r.Discoveries.Select(d => d.Index));
        Assert.Equal(3, s.DiscoveryCount);
        Assert.Equal(new[] { "cotton_cloud", "old_satellite", "pebble_moon" }, s.FoundAt.Keys.Order());
        double rewards = Balance.DiscoveryReward(0, 0) + Balance.DiscoveryReward(0, 1) + Balance.DiscoveryReward(0, 2);
        Assert.Equal(3, s.Stats.Discoveries);
        Assert.Equal(target * Balance.DustPerStep + rewards, s.Stardust, 6);
    }

    [Fact]
    public void DiscoveriesContinueAsNamelessStarsAfterTheCatalog()
    {
        var s = NewGame();
        int catalog = Destinations.MilkyWay.Catalog.Count;
        s.DiscoveryCount = catalog;
        s.Distance = Balance.DiscoveryDistance(0, s.DiscoveryCount) - 0.5;
        var r = GameRules.Advance(s, 1);

        var d = Assert.Single(r.Discoveries);
        Assert.Equal(Destinations.NamelessSprite, d.Sprite);
        Assert.Equal(catalog + 1, s.DiscoveryCount);
        Assert.Empty(s.FoundAt); // 이름 없는 별은 도감에 따로 적지 않는다
    }

    [Fact]
    public void CatchUpIsCappedByTheOfflineLimit()
    {
        var s = NewGame();
        var r = GameRules.CatchUp(s, T0.AddDays(3));
        Assert.Equal(GameRules.OfflineCap(0).TotalSeconds, r.Seconds, 6);
        Assert.Equal(T0.AddDays(3), s.LastTick);
    }

    [Fact]
    public void CatchUpReportsTheSecondsDroppedByTheCap()
    {
        var s = NewGame();
        double cap = GameRules.OfflineCap(0).TotalSeconds;
        var r = GameRules.CatchUp(s, T0.AddSeconds(cap + 3600));
        Assert.Equal(3600, r.CappedSeconds, 6);

        // 상한 안쪽이면 버린 시간이 없다 (경계: 딱 상한만큼)
        Assert.Equal(0, GameRules.CatchUp(s, s.LastTick.AddSeconds(cap)).CappedSeconds);

        // 침낭 연구로 상한이 늘면 같은 공백도 덜 버린다
        var bunk = NewGame();
        bunk.Upgrades[Upgrades.Bunk] = 1;
        var rb = GameRules.CatchUp(bunk, T0.AddSeconds(cap + 3600));
        Assert.Equal(Math.Max(0, cap + 3600 - GameRules.OfflineCap(1).TotalSeconds), rb.CappedSeconds, 6);
        Assert.True(rb.CappedSeconds < r.CappedSeconds);
    }

    [Fact]
    public void ClockGoingBackwardsGivesNothing()
    {
        var s = NewGame();
        var r = GameRules.CatchUp(s, T0.AddHours(-5));
        Assert.Equal(0, r.Seconds);
        Assert.Equal(0, s.Distance);
        Assert.Equal(T0.AddHours(-5), s.LastTick);
    }

    [Fact]
    public void SailRechargesUpToCapacityOnly()
    {
        var s = NewGame();
        s.Upgrades[Upgrades.Sail] = 10; // 0.2/초
        double cap = GameRules.BatteryCapacity(s);
        s.Starlight = cap - 1;
        GameRules.Advance(s, 100);
        Assert.Equal(cap, s.Starlight, 6);
    }

    [Fact]
    public void BuyingSpendsStardustAndRespectsMaxLevel()
    {
        var s = NewGame();
        var crew = Upgrades.Get(Upgrades.Crew);
        s.Stardust = 1e12;
        for (int i = 0; i < crew.MaxLevel; i++) Assert.True(GameRules.TryBuy(s, Upgrades.Crew));
        Assert.False(GameRules.TryBuy(s, Upgrades.Crew));
        Assert.Equal(crew.MaxLevel, s.Level(Upgrades.Crew));

        var poor = NewGame();
        poor.Stardust = Upgrades.Get(Upgrades.Engine).CostAt(0) - 1;
        Assert.False(GameRules.TryBuy(poor, Upgrades.Engine));
        poor.Stardust += 1;
        Assert.True(GameRules.TryBuy(poor, Upgrades.Engine));
        Assert.Equal(0, poor.Stardust);
    }

    [Fact]
    public void CrewEarnStardustEvenWithoutMoving()
    {
        var s = NewGame();
        s.Upgrades[Upgrades.Crew] = 3;
        var r = GameRules.Advance(s, 10);
        Assert.Equal(10 * Balance.DustPerStep + 3 * Balance.DustPerCrewPerSecond * 10, r.StardustGained, 6);
    }
}
