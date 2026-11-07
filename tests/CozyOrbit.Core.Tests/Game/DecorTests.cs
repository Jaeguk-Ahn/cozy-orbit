using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class DecorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static GameState NewGame() => GameState.New(Now);

    /// <summary>파는 소품을 종류별로 다 산 상태</summary>
    private static GameState RichGame()
    {
        var s = NewGame();
        s.Candy.Balance = 100_000;
        foreach (var d in Decor.All.Where(x => !x.IsSouvenir)) Assert.True(Decor.TryBuy(s, d.Id));
        return s;
    }

    private static string[] BuyableIds(DecorKind kind) =>
        Decor.All.Where(x => !x.IsSouvenir && x.Kind == kind).Select(x => x.Id).ToArray();

    [Fact]
    public void CatalogIsConsistent()
    {
        Assert.Equal(Decor.All.Count, Decor.All.Select(d => d.Id).Distinct().Count());
        foreach (var d in Decor.All)
        {
            Assert.Equal("decor_" + d.Id, d.Sprite);
            if (d.IsSouvenir) Assert.Equal(0, d.Price);
            else Assert.InRange(d.Price, 30, 200);
        }
        Assert.Equal(11, Decor.All.Count(d => !d.IsSouvenir));
        Assert.Equal(14, Decor.All.Count(d => d.IsSouvenir));
    }

    [Fact]
    public void BuyingSpendsCandyAndOwns()
    {
        var s = NewGame();
        var clock = Decor.Get("clock");
        s.Candy.Balance = clock.Price + 0.5;

        Assert.False(Decor.Owns(s, "clock"));
        Assert.True(Decor.TryBuy(s, "clock"));
        Assert.Equal(0.5, s.Candy.Balance, 9);
        Assert.Equal(clock.Price, s.Stats.CandySpent, 9);
        Assert.True(Decor.Owns(s, "clock"));
        Assert.Contains("clock", s.OwnedDecor);
        Assert.False(Decor.IsPlaced(s, "clock")); // 사기만 하고 놓지는 않는다

        s.Candy.Balance = 10_000;
        Assert.False(Decor.TryBuy(s, "clock")); // 두 번 살 수 없다
    }

    [Fact]
    public void CannotBuyWithoutEnoughCandyOrUnknownOrSouvenir()
    {
        var s = NewGame();
        var aquarium = Decor.Get("aquarium");
        s.Candy.Balance = aquarium.Price - 0.01;
        Assert.False(Decor.TryBuy(s, "aquarium"));
        Assert.False(Decor.Owns(s, "aquarium"));
        Assert.Equal(aquarium.Price - 0.01, s.Candy.Balance, 9);

        s.Candy.Balance = 10_000;
        Assert.False(Decor.TryBuy(s, "gone_item"));
        Assert.False(Decor.TryBuy(s, "saturn_lamp"));
        Assert.False(Decor.Owns(s, "saturn_lamp"));
        Assert.Equal(10_000, s.Candy.Balance, 9);
    }

    [Fact]
    public void SouvenirIsOwnedOnceItsDiscoveryIsFound()
    {
        var s = NewGame();
        Assert.False(Decor.Owns(s, "saturn_lamp"));
        Assert.False(Decor.Place(s, "saturn_lamp"));

        s.FoundAt["saturn"] = Now;
        Assert.True(Decor.Owns(s, "saturn_lamp"));
        Assert.True(Decor.Place(s, "saturn_lamp"));
        Assert.Empty(s.OwnedDecor); // 기념품은 산 목록에 적지 않는다
    }

    [Fact]
    public void SouvenirLookupByDiscovery()
    {
        Assert.Equal("saturn_lamp", Decor.SouvenirFor("saturn")?.Id);
        Assert.Equal("cheese_cushion", Decor.SouvenirFor("cheese_moon")?.Id);
        Assert.Equal("golden_record", Decor.SouvenirFor("voyager_1")?.Id);
        Assert.Null(Decor.SouvenirFor("jupiter"));
        Assert.Null(Decor.SouvenirFor("nope"));
    }

    [Fact]
    public void SouvenirsPointAtMilkyWayDiscoveriesThatExist()
    {
        // 다른 목적지의 도감은 아직 채우는 중일 수 있어 우리 은하 쪽만 확인한다
        string[] milkyWay = { "cheese_moon", "old_satellite", "music_box", "lantern_star", "golden_acorn", "home_letter", "saturn", "moon", "crab_nebula", "voyager_1", "pleiades" };
        foreach (var id in milkyWay)
        {
            Assert.NotNull(Decor.SouvenirFor(id));
            Assert.NotNull(Destinations.Find(id));
        }
    }

    [Fact]
    public void OnlyOwnedItemsCanBePlaced()
    {
        var s = NewGame();
        Assert.False(Decor.Place(s, "plant"));
        Assert.False(Decor.Place(s, "gone_item"));
        Assert.Empty(s.PlacedDecor);
    }

    [Fact]
    public void PlacementIsLimitedPerKind()
    {
        var s = RichGame();
        var floor = BuyableIds(DecorKind.Floor);
        var wall = BuyableIds(DecorKind.Wall);

        for (int i = 0; i < Decor.MaxPerKind; i++) Assert.True(Decor.Place(s, floor[i]));
        Assert.False(Decor.CanPlace(s, floor[3]));
        Assert.False(Decor.Place(s, floor[3])); // 바닥이 꽉 찼다

        // 바닥이 꽉 차도 벽은 따로 센다
        for (int i = 0; i < Decor.MaxPerKind; i++) Assert.True(Decor.Place(s, wall[i]));
        Assert.False(Decor.Place(s, wall[3]));

        Assert.Equal(floor.Take(3), Decor.Placed(s, DecorKind.Floor).Select(d => d.Id));
        Assert.Equal(wall.Take(3), Decor.Placed(s, DecorKind.Wall).Select(d => d.Id));
    }

    [Fact]
    public void PlacingTwiceFailsAndRemoveFreesTheSlot()
    {
        var s = RichGame();
        var floor = BuyableIds(DecorKind.Floor);
        foreach (var id in floor.Take(3)) Decor.Place(s, id);

        Assert.False(Decor.Place(s, floor[0])); // 이미 놓았다
        Assert.True(Decor.Remove(s, floor[1]));
        Assert.False(Decor.IsPlaced(s, floor[1]));
        Assert.False(Decor.Remove(s, floor[1])); // 이미 치웠다
        Assert.Equal(new[] { floor[0], floor[2] }, Decor.Placed(s, DecorKind.Floor).Select(d => d.Id));

        Assert.True(Decor.Place(s, floor[3])); // 빈자리에
        Assert.Equal(new[] { floor[0], floor[2], floor[3] }, Decor.Placed(s, DecorKind.Floor).Select(d => d.Id));
        Assert.True(Decor.Owns(s, floor[1])); // 치워도 가진 건 그대로
    }

    [Fact]
    public void StaleIdsInPlacedAreIgnored()
    {
        var s = RichGame();
        var floor = BuyableIds(DecorKind.Floor);
        s.PlacedDecor.AddRange(new[]
        {
            "gone_item",        // 없어진 소품
            "moon_globe",       // 아직 발견하지 않은 기념품
            floor[0],
            floor[0],           // 중복
            "clock",            // 다른 종류
            floor[1],
        });

        Assert.Equal(new[] { floor[0], floor[1] }, Decor.Placed(s, DecorKind.Floor).Select(d => d.Id));
        Assert.Equal(new[] { "clock" }, Decor.Placed(s, DecorKind.Wall).Select(d => d.Id));
        Assert.False(Decor.IsPlaced(s, "moon_globe"));
        Assert.False(Decor.Remove(s, "gone_item"));

        // 안 쳐 주는 기록은 자리를 차지하지 않는다
        Assert.True(Decor.Place(s, floor[2]));
        Assert.False(Decor.Place(s, floor[3]));

        // 나중에 발견하면 옛 기록이 살아나지만 자리 수는 넘지 않는다
        s.FoundAt["moon"] = Now;
        Assert.Equal(Decor.MaxPerKind, Decor.Placed(s, DecorKind.Floor).Count);
    }
}
