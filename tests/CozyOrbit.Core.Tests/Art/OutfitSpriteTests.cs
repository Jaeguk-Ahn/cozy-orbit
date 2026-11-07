using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Art;

public class OutfitSpriteTests
{
    public static TheoryData<string> OutfitSprites()
    {
        var data = new TheoryData<string>();
        foreach (var skin in Skins.All.Where(s => s.Slot == SkinSlot.Outfit && s.Sprite != null)) data.Add(skin.Sprite!);
        return data;
    }

    [Theory]
    [MemberData(nameof(OutfitSprites))]
    public void Outfit_sprite_fits_overlay_canvas(string id)
    {
        var sprite = SpriteLibrary.Get(id);
        Assert.Equal(Skins.OutfitWidth, sprite.Width);
        Assert.Equal(Skins.OutfitHeight, sprite.Height);
        Assert.True(sprite.FrameCount is 1 or 4, $"{id}: 프레임 {sprite.FrameCount}");
    }

    [Theory]
    [InlineData(0), InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5)]
    public void Hamster_head_rows_match_across_idle_and_walk_frames(int fur)
    {
        // 복장이 어긋나지 않게: 대기(0)·걸음(2, 3)의 0~12 행이 같아야 한다
        var sprite = SpriteLibrary.Get($"hamster_{fur}");
        Assert.Equal(4, sprite.FrameCount);
        for (var y = 0; y <= 12; y++)
        for (var x = 0; x < sprite.Width; x++)
        {
            Assert.Equal(sprite.Pixel(0, x, y), sprite.Pixel(2, x, y));
            Assert.Equal(sprite.Pixel(0, x, y), sprite.Pixel(3, x, y));
        }
    }
}
