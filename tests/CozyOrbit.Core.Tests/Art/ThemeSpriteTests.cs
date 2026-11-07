using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Art;

public class ThemeSpriteTests
{
    [Fact]
    public void Every_theme_skin_resolves_to_its_own_theme()
    {
        foreach (var skin in Skins.All.Where(s => s.Slot == SkinSlot.Theme))
        {
            Assert.Contains(SceneThemes.All, t => t.Id == skin.ThemeId);
            Assert.Equal(skin.ThemeId, SceneThemes.Get(skin.ThemeId).Id);
        }
    }

    [Fact]
    public void Every_theme_sprite_exists()
    {
        foreach (var theme in SceneThemes.All)
        {
            Assert.True(SpriteLibrary.TryGet(theme.StarSmall, out _), $"{theme.Id}: {theme.StarSmall}");
            Assert.True(SpriteLibrary.TryGet(theme.StarBig, out _), $"{theme.Id}: {theme.StarBig}");
            foreach (var deco in theme.Decorations)
                Assert.True(SpriteLibrary.TryGet(deco.Sprite, out _), $"{theme.Id}: {deco.Sprite}");
        }
    }
}
