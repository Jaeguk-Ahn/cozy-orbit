using CozyOrbit.Core.Art;

namespace CozyOrbit.Core.Tests.Art;

public class SpriteLibraryTests
{
    private static readonly string[] ObjectIds =
    [
        "pebble_moon", "cotton_cloud", "old_satellite", "tiny_comet", "bunny_rock", "star_seed",
        "crystal_rock", "donut_rock", "cheese_moon", "mushroom_rock", "ice_cube", "lost_mitten",
        "jelly_giant", "candy_planet", "teacup_planet", "bubble_planet", "sleepy_storm", "twin_berries",
        "pink_nebula", "whale_cloud", "lantern_star", "flower_nebula", "owl_stars", "music_box",
        "sleepy_blackhole", "golden_acorn", "star_cradle", "rainbow_pulsar", "space_lighthouse", "home_letter",
        "nameless_star",
    ];

    private static readonly string[] IconIds =
    [
        "starlight", "stardust", "distance", "engine", "battery", "sail", "booster", "scope",
        "crew", "bunk", "lab", "book", "log", "gear", "unknown",
    ];

    public static TheoryData<string, int, int, int> Required()
    {
        var data = new TheoryData<string, int, int, int>
        {
            // id, 너비, 높이, 최소 프레임 수 (크기 0 = 범위만 검사)
            { "probe", 24, 24, 2 },
            { "probe_boost", 24, 24, 3 },
            { "station", 48, 32, 2 },
            { "star_small", 3, 3, 2 },
            { "star_big", 5, 5, 2 },
            { "sparkle", 5, 5, 3 },
        };
        for (var i = 0; i < 6; i++) data.Add($"hamster_{i}", 16, 16, 4);
        foreach (var id in IconIds) data.Add($"icon_{id}", 12, 12, 1);
        foreach (var id in ObjectIds) data.Add($"obj_{id}", 0, 0, 1);
        return data;
    }

    [Theory]
    [MemberData(nameof(Required))]
    public void Required_sprite_exists_with_expected_size(string id, int width, int height, int minFrames)
    {
        var sprite = SpriteLibrary.Get(id);
        Assert.True(sprite.FrameCount >= minFrames, $"{id}: 프레임 {sprite.FrameCount}");
        if (width > 0)
        {
            Assert.Equal(width, sprite.Width);
            Assert.Equal(height, sprite.Height);
        }
        else
        {
            Assert.InRange(sprite.Width, 16, 24);
            Assert.InRange(sprite.Height, 16, 24);
        }
    }

    [Fact]
    public void All_loads_and_every_sprite_has_visible_pixels()
    {
        Assert.NotEmpty(SpriteLibrary.All);
        foreach (var sprite in SpriteLibrary.All)
        {
            Assert.True(sprite.FrameSeconds > 0, sprite.Id);
            for (var f = 0; f < sprite.FrameCount; f++)
            {
                var visible = false;
                for (var y = 0; y < sprite.Height && !visible; y++)
                for (var x = 0; x < sprite.Width && !visible; x++)
                    visible = sprite.Pixel(f, x, y) != 0;
                Assert.True(visible, $"{sprite.Id} 프레임 {f} 이 비어 있음");
            }
        }
    }

    [Fact]
    public void Missing_id_throws_and_TryGet_returns_false()
    {
        Assert.Throws<KeyNotFoundException>(() => SpriteLibrary.Get("no_such_sprite"));
        Assert.False(SpriteLibrary.TryGet("no_such_sprite", out _));
    }

    [Fact]
    public void Parse_names_sprite_on_bad_grid()
    {
        var map = new Dictionary<char, uint> { ['k'] = Palette.Outline };
        var ex = Assert.Throws<InvalidOperationException>(() => SpriteLibrary.Parse("bad_one", 1, map, [["kk", "k"]]));
        Assert.Contains("bad_one", ex.Message);
        ex = Assert.Throws<InvalidOperationException>(() => SpriteLibrary.Parse("bad_two", 1, map, [["kx"]]));
        Assert.Contains("bad_two", ex.Message);
    }
}
