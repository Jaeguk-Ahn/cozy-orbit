using Avalonia;
using Avalonia.Media;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.App.Rendering;

/// <summary>장착한 스킨 → 그릴 스프라이트·테마 색. 장면(큰 창·플로팅)·상점 미리보기가 같은 규칙을 쓴다.</summary>
public static class SkinArt
{
    /// <summary>햄찌 프레임 번호 (SpriteLibrary.Characters 와 약속)</summary>
    public const int FrameIdle = 0, FrameBlink = 1, FrameStepA = 2, FrameStepB = 3;

    /// <summary>탐사선 평소/부스트 스프라이트. 스프라이트가 없으면(그림이 아직 없는 스킨) 도토리호로.</summary>
    public static (string Normal, string Boost) Ship(SkinDef ship) =>
        ship.Sprite != null && ship.BoostSprite != null && SpriteLibrary.TryGet(ship.Sprite, out _) && SpriteLibrary.TryGet(ship.BoostSprite, out _)
            ? (ship.Sprite, ship.BoostSprite)
            : ("probe", "probe_boost");

    public static (string Normal, string Boost) Ship(GameState s) => Ship(Shop.EquippedSkin(s, SkinSlot.Ship));

    public static string CaptainFur(GameState s) => Shop.EquippedSkin(s, SkinSlot.Fur).Sprite ?? "hamster_0";

    public static string? Outfit(GameState s) => Shop.EquippedSkin(s, SkinSlot.Outfit).Sprite;

    public static SceneTheme Theme(GameState s) => SceneThemes.Get(Shop.EquippedSkin(s, SkinSlot.Theme).ThemeId);

    /// <summary>크루 i 번째(선장 제외)의 털색 — 선장 털색은 건너뛴다</summary>
    public static string CrewFur(string captainFur, int i)
    {
        int captain = int.TryParse(captainFur.AsSpan("hamster_".Length), out var c) ? c : 0;
        int index = i % 5;
        if (index >= captain) index++;
        return "hamster_" + index;
    }

    /// <summary>햄찌 + (있으면) 복장을 겹쳐 그린다. (x, y) = 햄찌 16×16 의 왼쪽 위.</summary>
    public static void DrawHamster(DrawingContext dc, string fur, string? outfit, int frame, double x, double y, double scale, double t, bool flipX = false)
    {
        SpriteCache.Draw(dc, fur, frame, x, y, scale, flipX: flipX);
        if (outfit == null || !SpriteLibrary.TryGet(outfit, out var o)) return;
        // 복장 프레임: 4장이면 햄찌 프레임과 맞추고, 아니면 제 박자로
        int of = o.FrameCount == 4 ? frame : SpriteCache.FrameAt(outfit, t);
        SpriteCache.Draw(dc, outfit, of, x - Skins.OutfitOriginX * scale, y - Skins.OutfitOriginY * scale, scale, flipX: flipX);
    }

    private static readonly Dictionary<uint, IBrush> Solids = new();

    public static IBrush Solid(uint argb)
    {
        if (!Solids.TryGetValue(argb, out var b)) Solids[argb] = b = new SolidColorBrush(Color.FromUInt32(argb));
        return b;
    }

    private static readonly Dictionary<string, IBrush> Skies = new();

    /// <summary>테마 하늘 그라데이션 (위 → 아래)</summary>
    public static IBrush Sky(SceneTheme theme)
    {
        if (Skies.TryGetValue(theme.Id, out var b)) return b;
        b = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromUInt32(theme.SkyTop), 0),
                new GradientStop(Color.FromUInt32(theme.SkyBottom), 1),
            },
        };
        Skies[theme.Id] = b;
        return b;
    }
}
