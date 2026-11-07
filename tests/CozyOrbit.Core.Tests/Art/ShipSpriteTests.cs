using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Art;

public class ShipSpriteTests
{
    public static TheoryData<string> ShipIds()
    {
        var data = new TheoryData<string>();
        foreach (var skin in Skins.All.Where(s => s.Slot == SkinSlot.Ship)) data.Add(skin.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(ShipIds))]
    public void 탐사선_스킨은_평소와_부스트_스프라이트가_있다(string id)
    {
        var skin = Skins.All.Single(s => s.Id == id);

        Assert.NotNull(skin.Sprite);
        Assert.NotNull(skin.BoostSprite);
        var normal = SpriteLibrary.Get(skin.Sprite);
        var boost = SpriteLibrary.Get(skin.BoostSprite);

        Assert.Equal((24, 24), (normal.Width, normal.Height));
        Assert.True(normal.FrameCount >= 2, $"{normal.Id}: 프레임 {normal.FrameCount}");
        Assert.Equal((24, 24), (boost.Width, boost.Height));
        Assert.True(boost.FrameCount >= 3, $"{boost.Id}: 프레임 {boost.FrameCount}");
    }
}
