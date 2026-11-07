using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class VoyagesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static GameState NewGame()
    {
        var s = GameState.New(T0);
        s.Settings.AutoBoost = false;
        return s;
    }

    [Fact]
    public void CannotDepartBeforeTheAtlasIsComplete()
    {
        var s = NewGame();
        s.DiscoveryCount = Destinations.MilkyWay.Catalog.Count - 1;
        Assert.False(Voyages.CanDepart(s));
        Assert.False(Voyages.Depart(s));
        Assert.Equal(0, s.Voyage);
    }

    [Fact]
    public void DepartingResetsTheRunButKeepsCollectionsAndPaysCandy()
    {
        var s = NewGame();
        s.DiscoveryCount = Destinations.MilkyWay.Catalog.Count;
        s.Distance = 1e9;
        s.Stardust = 12345;
        s.Starlight = 600;
        s.Upgrades[Upgrades.Engine] = 9;
        s.FoundAt["moon"] = T0;
        s.OwnedSkins.Add("ship_ufo");
        s.Candy.Balance = 5;
        double candy = Voyages.DepartureCandy(s);

        Assert.True(Voyages.Depart(s));

        Assert.Equal(1, s.Voyage);
        Assert.Equal(0, s.Distance);
        Assert.Equal(0, s.DiscoveryCount);
        Assert.Equal(0, s.Stardust);
        Assert.Empty(s.Upgrades);
        Assert.Equal(600, s.Starlight);
        Assert.Contains("moon", s.FoundAt.Keys);
        Assert.Contains("ship_ufo", s.OwnedSkins);
        Assert.Equal(5 + candy, s.Candy.Balance);
        Assert.Equal("andromeda", Destinations.Current(s).Id);
    }

    [Fact]
    public void EachVoyageMultipliesSpeedAndStardustForGood()
    {
        var s = NewGame();
        double before = GameRules.Speed(s);
        double dustBefore = GameRules.DustMultiplier(s);
        s.Voyage = 2;
        Assert.Equal(before * Balance.VoyageBonus * Balance.VoyageBonus, GameRules.Speed(s), 9);
        Assert.Equal(dustBefore * Balance.VoyageBonus * Balance.VoyageBonus, GameRules.DustMultiplier(s), 9);
    }

    [Fact]
    public void DiscoveriesInALaterVoyageUseThatDestinationsCatalog()
    {
        var s = NewGame();
        s.Voyage = 1;
        var r = GameRules.Advance(s, 1);
        r = GameRules.Advance(s, Balance.DiscoveryDistance(1, 0) / GameRules.Speed(s) + 1);

        var first = Destinations.All[1].Catalog[0];
        Assert.Contains(r.Discoveries, d => d.Sprite == first.Sprite);
        Assert.Contains(first.Id, s.FoundAt.Keys);
    }

    [Fact]
    public void AfterTheLastDestinationVoyagesContinueIntoEndlessSpace()
    {
        var s = NewGame();
        s.Voyage = Destinations.All.Count;
        var dest = Destinations.Current(s);
        Assert.True(dest.IsEndless);
        s.DiscoveryCount = Destinations.EndlessGoal;
        Assert.True(Voyages.CanDepart(s));
        Assert.True(Voyages.Depart(s));
        Assert.True(Destinations.Current(s).IsEndless);
    }

    [Fact]
    public void EveryDestinationHasUniqueIdsAndValidSectors()
    {
        var ids = Destinations.All.SelectMany(d => d.Catalog).Select(x => x.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var d in Destinations.All)
        {
            Assert.NotEmpty(d.Catalog);
            Assert.All(d.Catalog, x => Assert.InRange(x.Sector, 1, d.Sectors.Count));
            Assert.All(d.Catalog, x => Assert.False(string.IsNullOrWhiteSpace(x.NoteKo) || string.IsNullOrWhiteSpace(x.NoteEn), x.Id));
        }
    }

    [Fact]
    public void ShootingStarsPayAboutFortyFiveSecondsOfIncome()
    {
        var s = NewGame();
        double reward = GameRules.CatchShootingStar(s);
        Assert.Equal(Math.Max(Balance.ShootingStarMinReward, Math.Round(GameRules.DustPerSecond(s) * Balance.ShootingStarSeconds)), reward);
        Assert.Equal(1, s.Stats.ShootingStars);
        Assert.Equal(reward, s.Stardust, 9);
        Assert.True(GameRules.ShootingStarReward(s, small: true) < reward);
    }
}
