using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class AchievementsTests
{
    private static GameState NewGame() => GameState.New(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void CatalogIsWellFormed()
    {
        Assert.InRange(Achievements.All.Count, 30, 45);
        Assert.Equal(Achievements.All.Count, Achievements.All.Select(a => a.Id).Distinct().Count());
        var s = NewGame();
        foreach (var a in Achievements.All)
        {
            Assert.InRange(a.Reward, 10, 200);
            Assert.True(a.Target() > 0, a.Id);
            Assert.False(Achievements.IsDone(s, a), a.Id);  // 새 게임에서 바로 받을 수 있는 것은 없다
        }
    }

    [Fact]
    public void ClaimOnlyOnceAndOnlyWhenDone()
    {
        var s = NewGame();
        var pets = Achievements.Find("pets_100")!;

        s.Stats.Pets = 99;
        Assert.Equal(0.99, Achievements.Progress(s, pets), 9);
        Assert.False(Achievements.TryClaim(s, pets.Id));
        Assert.Empty(s.Achievements);

        s.Stats.Pets = 100;
        Assert.True(Achievements.CanClaim(s, pets));
        Assert.True(Achievements.TryClaim(s, pets.Id));
        Assert.Contains(pets.Id, s.Achievements);
        Assert.Equal(pets.Reward, s.Candy.Balance);
        Assert.Equal(pets.Reward, s.Candy.Earned);

        Assert.False(Achievements.TryClaim(s, pets.Id));
        Assert.Equal(pets.Reward, s.Candy.Balance);
        Assert.False(Achievements.TryClaim(s, "no_such_achievement"));
    }

    [Fact]
    public void MilkyWayAtlasNeedsEveryObject()
    {
        var s = NewGame();
        var atlas = Achievements.Find("atlas_milky_way")!;
        var catalog = Destinations.MilkyWay.Catalog;
        foreach (var d in catalog.Skip(1)) s.FoundAt[d.Id] = s.CreatedAt;
        Assert.False(Achievements.IsDone(s, atlas));
        s.FoundAt[catalog[0].Id] = s.CreatedAt;
        Assert.True(Achievements.IsDone(s, atlas));
    }

    [Fact]
    public void VoyageAndSkinAchievementsReadState()
    {
        var s = NewGame();
        s.Voyage = 1;
        Assert.True(Achievements.IsDone(s, Achievements.Find("voyage_first")!));

        // 무료 기본 스킨은 세지 않는다
        foreach (var skin in Skins.All.Where(x => x.IsFree)) s.OwnedSkins.Add(skin.Id);
        Assert.Equal(0, Achievements.Progress(s, Achievements.Find("skins_5")!));
        foreach (var skin in Skins.All.Where(x => !x.IsFree)) s.OwnedSkins.Add(skin.Id);
        Assert.True(Achievements.IsDone(s, Achievements.Find("skins_all")!));
    }

    [Fact]
    public void EveryChartedDestinationHasAnAtlasAchievement()
    {
        var s = NewGame();
        foreach (var d in Destinations.All)
        {
            var atlas = Achievements.Find("atlas_" + d.Id)!;
            Assert.Equal(d.Catalog.Count, atlas.Target());
            Assert.False(Achievements.IsDone(s, atlas), d.Id);
        }
        var andromeda = Achievements.Find("atlas_andromeda")!;
        foreach (var x in Destinations.Andromeda.Catalog) s.FoundAt[x.Id] = s.CreatedAt;
        Assert.True(Achievements.IsDone(s, andromeda));
        Assert.False(Achievements.IsDone(s, Achievements.Find("atlas_milky_way")!));
    }

    [Fact]
    public void WhaleResearchAndSectorAchievementsReadStats()
    {
        var s = NewGame();
        var whale = Achievements.Find("whale_5")!;
        s.Stats.WhaleGreets = 4;
        Assert.Equal(0.8, Achievements.Progress(s, whale), 9);
        s.Stats.WhaleGreets = 5;
        Assert.True(Achievements.IsDone(s, whale));

        s.Stats.ResearchBought = 49;
        Assert.False(Achievements.IsDone(s, Achievements.Find("research_50")!));
        s.Stats.ResearchBought = 50;
        Assert.True(Achievements.IsDone(s, Achievements.Find("research_50")!));

        // 구역 완성 보상을 받으면 구역 도전과제가 찬다
        var sectors = Achievements.Find("sectors_10")!;
        s.DiscoveryCount = Destinations.MilkyWay.Catalog.Count;
        Sectors.ClaimCompleted(s);
        Assert.True(Achievements.IsDone(s, sectors));
        var all = Achievements.Find("sectors_all")!;
        Assert.Equal(Destinations.All.Sum(d => d.Sectors.Count), all.Target());
        Assert.False(Achievements.IsDone(s, all));
    }
}
