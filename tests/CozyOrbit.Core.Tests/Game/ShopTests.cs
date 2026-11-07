using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class ShopTests
{
    private static GameState NewGame() => GameState.New(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void DefaultsAreOwnedAndEquippedFromTheStart()
    {
        var s = NewGame();
        foreach (var slot in Enum.GetValues<SkinSlot>())
        {
            Assert.Equal(Skins.DefaultFor(slot), Shop.Equipped(s, slot));
            Assert.True(Shop.Owns(s, Skins.DefaultFor(slot)));
        }
    }

    [Fact]
    public void BuyingSpendsCandyOwnsAndEquips()
    {
        var s = NewGame();
        var helmet = Skins.Get("outfit_helmet");
        s.Candy.Balance = helmet.Price + 0.5;

        Assert.True(Shop.TryBuy(s, helmet.Id));
        Assert.Equal(0.5, s.Candy.Balance, 9);
        Assert.True(Shop.Owns(s, helmet.Id));
        Assert.Equal(helmet.Id, Shop.Equipped(s, SkinSlot.Outfit));
        Assert.False(Shop.TryBuy(s, helmet.Id)); // 두 번 살 수 없다
    }

    [Fact]
    public void CannotBuyWithoutEnoughCandy()
    {
        var s = NewGame();
        var ufo = Skins.Get("ship_ufo");
        s.Candy.Balance = ufo.Price - 0.01;
        Assert.False(Shop.TryBuy(s, ufo.Id));
        Assert.False(Shop.Owns(s, ufo.Id));
        Assert.Equal(Skins.DefaultShip, Shop.Equipped(s, SkinSlot.Ship));
    }

    [Fact]
    public void OnlyOwnedSkinsCanBeEquippedAndSwitchingBackIsFree()
    {
        var s = NewGame();
        Assert.False(Shop.Equip(s, "theme_candy"));

        s.Candy.Balance = 10_000;
        Shop.TryBuy(s, "theme_candy");
        Assert.True(Shop.Equip(s, Skins.DefaultTheme));
        Assert.Equal(Skins.DefaultTheme, Shop.Equipped(s, SkinSlot.Theme));
        Assert.True(Shop.Equip(s, "theme_candy"));
        Assert.Equal("theme_candy", Shop.Equipped(s, SkinSlot.Theme));
    }

    [Fact]
    public void BrokenEquippedValuesFallBackToDefaults()
    {
        var s = NewGame();
        s.Equipped[SkinSlot.Ship] = "ship_gone";            // 없어진 스킨
        s.Equipped[SkinSlot.Outfit] = "ship_ufo";           // 다른 자리의 스킨
        s.Equipped[SkinSlot.Theme] = "theme_candy";         // 안 산 스킨
        Assert.Equal(Skins.DefaultShip, Shop.Equipped(s, SkinSlot.Ship));
        Assert.Equal(Skins.DefaultOutfit, Shop.Equipped(s, SkinSlot.Outfit));
        Assert.Equal(Skins.DefaultTheme, Shop.Equipped(s, SkinSlot.Theme));
    }
}
