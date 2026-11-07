using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public sealed class SaveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cozy-orbit-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public SaveStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    [Fact]
    public void RoundTripKeepsProgressLedgerAndSettings()
    {
        var store = new SaveStore(Path.Combine(_dir, "save.json"));
        var s = GameState.New(Now);
        s.Distance = 1234.5;
        s.Stardust = 99;
        s.Starlight = 42;
        s.Upgrades[Upgrades.Engine] = 3;
        s.DiscoveryCount = 2;
        s.FoundAt["pebble_moon"] = Now;
        s.Ledger.CreditedWeighted["2026-11-07"] = 12345;
        s.Ledger.LastScanAt = Now;
        s.Settings.AutoBoost = false;
        s.Settings.DisabledProviders.Add("codex");
        s.Candy.Balance = 12.5;
        CandyLedger.BeginSession(s.Candy, Now);
        s.Candy.Sessions[0].CreditedWeighted = 777;
        s.OwnedSkins.Add("ship_ufo");
        s.Equipped[SkinSlot.Ship] = "ship_ufo";
        store.Save(s);

        var back = store.Load(Now.AddMinutes(1));
        Assert.Equal(1234.5, back.Distance);
        Assert.Equal(99, back.Stardust);
        Assert.Equal(42, back.Starlight);
        Assert.Equal(3, back.Level(Upgrades.Engine));
        Assert.Equal(Now, back.FoundAt["pebble_moon"]);
        Assert.Equal(12345, back.Ledger.CreditedWeighted["2026-11-07"]);
        Assert.Equal(s.Ledger.StartedAt, back.Ledger.StartedAt);
        Assert.False(back.Settings.AutoBoost);
        Assert.Contains("codex", back.Settings.DisabledProviders);
        Assert.Equal(12.5, back.Candy.Balance);
        Assert.Equal(777, Assert.Single(back.Candy.Sessions).CreditedWeighted);
        Assert.Equal("ship_ufo", Shop.Equipped(back, SkinSlot.Ship));
    }

    [Fact]
    public void SaveFromBeforeTheShopLoadsWithEmptyWallet()
    {
        var path = Path.Combine(_dir, "save.json");
        File.WriteAllText(path, """{ "version": 1, "distance": 10, "stardust": 5 }""");
        var s = new SaveStore(path).Load(Now);
        Assert.Equal(10, s.Distance);
        Assert.Equal(0, s.Candy.Balance);
        Assert.Empty(s.Candy.Sessions);
        Assert.Equal(Skins.DefaultOutfit, Shop.Equipped(s, SkinSlot.Outfit));
    }

    [Fact]
    public void V1SaveRebuildsTheAtlasForTheNewMilkyWayOrder()
    {
        // v1 순서: 6번째(0부터)가 crystal_rock. v2 에서는 그 자리에 실제 천체(달·금성)가 들어왔다.
        var path = Path.Combine(_dir, "save.json");
        File.WriteAllText(path, """
            { "version": 1, "distance": 5000, "discoveryCount": 8,
              "foundAt": { "pebble_moon": "2026-11-07T10:00:00+00:00", "crystal_rock": "2026-11-07T11:00:00+00:00" },
              "upgrades": { "engine": 2, "scope": 1 } }
            """);
        var s = new SaveStore(path).Load(Now);

        Assert.Equal(GameState.CurrentVersion, s.Version);
        Assert.Equal(Destinations.MilkyWay.Catalog.Take(8).Select(d => d.Id).Order(), s.FoundAt.Keys.Order());
        Assert.DoesNotContain("crystal_rock", s.FoundAt.Keys);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero), s.FoundAt["pebble_moon"]);
        Assert.Equal(8, s.Stats.Discoveries);
        Assert.Equal(3, s.Stats.ResearchBought);
    }

    [Fact]
    public void BrokenSaveIsMovedAsideNotDeleted()
    {
        var path = Path.Combine(_dir, "save.json");
        File.WriteAllText(path, "{ not json");
        var s = new SaveStore(path).Load(Now);

        Assert.Equal(0, s.Distance);
        Assert.False(File.Exists(path));
        var aside = Assert.Single(Directory.GetFiles(_dir, "save.json.broken-*"));
        Assert.Equal("{ not json", File.ReadAllText(aside));
    }
}
