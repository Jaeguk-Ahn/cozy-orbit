using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Art;

/// <summary>게임 데이터가 가리키는 스프라이트가 모두 있는지 — 빠지면 화면에 빈 칸이 생긴다</summary>
public class ContentSpriteTests
{
    public static TheoryData<string> DiscoverySprites()
    {
        var data = new TheoryData<string>();
        foreach (var d in Destinations.All)
            foreach (var x in d.Catalog) data.Add(x.Sprite);
        return data;
    }

    [Theory]
    [MemberData(nameof(DiscoverySprites))]
    public void EveryDiscoveryHasAnObjectSprite(string sprite)
    {
        Assert.True(SpriteLibrary.TryGet(sprite, out var s), sprite);
        // 막대·흐름처럼 길쭉한 천체는 납작할 수 있다 — 장면 배치가 감당하는 최대 24
        Assert.InRange(s!.Width, 8, 24);
        Assert.InRange(s.Height, 8, 24);
    }

    [Fact]
    public void EveryDestinationDecorAndSceneEffectHasASprite()
    {
        var ids = Destinations.All.Select(d => d.Sprite)
            .Concat(Decor.All.Select(d => d.Sprite))
            .Concat(new[] { Destinations.NamelessSprite, "fx_shooting_star", "fx_meteor", "fx_space_whale", "fx_heart", "fx_note", "fx_zzz",
                            "icon_goal", "icon_trophy", "icon_decor", "icon_chart", "icon_voyage", "icon_candy", "icon_shop" });
        var missing = ids.Where(id => !SpriteLibrary.TryGet(id, out _)).ToList();
        Assert.Empty(missing);
    }
}
