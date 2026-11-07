using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.App.Views;

/// <summary>
/// 상점 칸의 미리보기 (64×48). 복장은 지금 선장 털색 위에 겹치고, 탐사선은 날갯짓(불꽃)까지, 테마는 작은 장면으로.
/// </summary>
public sealed class SkinPreview : Control
{
    public const double BoxWidth = 64, BoxHeight = 48;

    private static readonly DispatcherTimer Timer = new(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => Tick?.Invoke());
    private static event Action? Tick;
    private static readonly DateTime Epoch = DateTime.UtcNow;

    public SkinDef? Skin { get; set; }
    /// <summary>복장 미리보기에 쓸 선장 털색 스프라이트</summary>
    public string CaptainFur { get; set; } = "hamster_0";

    public SkinPreview()
    {
        Width = BoxWidth;
        Height = BoxHeight;
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick += InvalidateVisual;
        Timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Tick -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext dc)
    {
        if (Skin is not { } skin) return;
        double t = (DateTime.UtcNow - Epoch).TotalSeconds;
        int blink = t % 3.1 < 0.15 ? SkinArt.FrameBlink : SkinArt.FrameIdle;
        switch (skin.Slot)
        {
            case SkinSlot.Outfit:
                // 20×22 캔버스 × 2 = 40×44 를 가운데에
                SkinArt.DrawHamster(dc, CaptainFur, skin.Sprite, blink,
                    Math.Round((BoxWidth - 40) / 2) + Skins.OutfitOriginX * 2, Math.Round((BoxHeight - 44) / 2) + Skins.OutfitOriginY * 2, 2, t);
                break;
            case SkinSlot.Fur when skin.Sprite != null:
                SpriteCache.Draw(dc, skin.Sprite, blink, (BoxWidth - 32) / 2, (BoxHeight - 32) / 2, 2);
                break;
            case SkinSlot.Ship:
                var (normal, boost) = SkinArt.Ship(skin);
                // 2초마다 1초씩 부스트 모습도 보여 준다
                string id = t % 2 < 1 ? normal : boost;
                SpriteCache.Draw(dc, id, SpriteCache.FrameAt(id, t), (BoxWidth - 48) / 2, 0, 2);
                break;
            case SkinSlot.Theme:
                DrawTheme(dc, SceneThemes.Get(skin.ThemeId), t);
                break;
        }
    }

    /// <summary>작은 장면: 하늘 + 점별 + 첫 장식 + 별 하나 + 연구소 벽·바닥</summary>
    private static void DrawTheme(DrawingContext dc, SceneTheme theme, double t)
    {
        const double labTop = 34;
        dc.FillRectangle(SkinArt.Sky(theme), new Rect(0, 0, BoxWidth, labTop));
        var dot = SkinArt.Solid(theme.StarDot);
        foreach (var (x, y) in new[] { (5.0, 4.0), (19, 13), (31, 6), (44, 21), (57, 9), (9, 25), (52, 28) })
            dc.FillRectangle(dot, new Rect(x, y, 1, 1));
        var deco = theme.Decorations.FirstOrDefault(d => SpriteLibrary.TryGet(d.Sprite, out _));
        if (deco != null)
        {
            var sprite = SpriteLibrary.Get(deco.Sprite);
            double scale = sprite.Width > 32 ? 0.5 : 1;
            SpriteCache.Draw(dc, deco.Sprite, SpriteCache.FrameAt(deco.Sprite, t), BoxWidth - sprite.Width * scale - 2, 2, scale);
        }
        string big = SpriteLibrary.TryGet(theme.StarBig, out _) ? theme.StarBig : "star_big";
        SpriteCache.Draw(dc, big, SpriteCache.FrameAt(big, t), 12, 9, 2);

        dc.FillRectangle(SkinArt.Solid(theme.Line), new Rect(0, labTop, BoxWidth, 1));
        dc.FillRectangle(SkinArt.Solid(theme.Wall), new Rect(0, labTop + 1, BoxWidth, 7));
        dc.FillRectangle(SkinArt.Solid(theme.Window), new Rect(8, labTop + 2, 5, 4));
        dc.FillRectangle(SkinArt.Solid(theme.Lamp), new Rect(32, labTop + 2, 2, 2));
        dc.FillRectangle(SkinArt.Solid(theme.FloorLight), new Rect(0, labTop + 8, BoxWidth, 1));
        dc.FillRectangle(SkinArt.Solid(theme.Floor), new Rect(0, labTop + 9, BoxWidth, BoxHeight - labTop - 9));
        dc.FillRectangle(SkinArt.Solid(theme.FloorDark), new Rect(20, labTop + 10, 1, 2));
        dc.FillRectangle(SkinArt.Solid(theme.FloorDark), new Rect(45, labTop + 11, 1, 2));
    }
}
