using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Art;

namespace CozyOrbit.App.Views;

/// <summary>스프라이트 하나를 정수 배율로 그리는 컨트롤. Animate 면 프레임을 돌린다.</summary>
public sealed class PixelIcon : Control
{
    public static readonly StyledProperty<string?> SpriteProperty = AvaloniaProperty.Register<PixelIcon, string?>(nameof(Sprite));
    public static readonly StyledProperty<int> ScaleProperty = AvaloniaProperty.Register<PixelIcon, int>(nameof(Scale), 2);
    public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<PixelIcon, bool>(nameof(Animate));
    /// <summary>true 면 실루엣(어둡게)으로 — 도감의 아직 못 만난 천체</summary>
    public static readonly StyledProperty<bool> DimProperty = AvaloniaProperty.Register<PixelIcon, bool>(nameof(Dim));

    static PixelIcon()
    {
        AffectsMeasure<PixelIcon>(SpriteProperty, ScaleProperty);
        AffectsRender<PixelIcon>(SpriteProperty, ScaleProperty, DimProperty);
    }

    public string? Sprite { get => GetValue(SpriteProperty); set => SetValue(SpriteProperty, value); }
    public int Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }
    public bool Dim { get => GetValue(DimProperty); set => SetValue(DimProperty, value); }

    private static readonly DispatcherTimer AnimTimer = new(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => AnimTick?.Invoke());
    private static event Action? AnimTick;
    private static readonly DateTime Epoch = DateTime.UtcNow;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Animate)
        {
            AnimTick += InvalidateVisual;
            AnimTimer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AnimTick -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Sprite != null && SpriteLibrary.TryGet(Sprite, out var s) ? new Size(s.Width * Scale, s.Height * Scale) : default;

    public override void Render(DrawingContext dc)
    {
        if (Sprite == null || !SpriteLibrary.TryGet(Sprite, out var s)) return;
        int frame = Animate ? SpriteCache.FrameAt(Sprite, (DateTime.UtcNow - Epoch).TotalSeconds) : 0;
        double x = Math.Round((Bounds.Width - s.Width * Scale) / 2);
        double y = Math.Round((Bounds.Height - s.Height * Scale) / 2);
        SpriteCache.Draw(dc, Sprite, frame, x, y, Scale, Dim ? 0.18 : 1);
    }
}
