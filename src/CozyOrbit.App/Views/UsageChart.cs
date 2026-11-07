using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CozyOrbit.Core.Art;

namespace CozyOrbit.App.Views;

/// <summary>막대 하나 — Label 은 아래 축(짧은 날짜), Detail 은 마우스를 올렸을 때 보이는 긴 이름</summary>
public readonly record struct ChartPoint(string Label, double Value, string? Detail = null);

/// <summary>
/// 도트 막대그래프. 모든 좌표를 2 DIP 격자(도트 한 칸)에 맞춰 안티에일리어싱 없이 그린다.
/// 가장 오른쪽(또는 HighlightIndex) 막대가 오늘 — 다른 색으로. 값이 모두 0 이어도 바닥 줄과 빈 칸 표시로 그린다.
/// </summary>
public sealed class UsageChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<UsageChart, IReadOnlyList<ChartPoint>?>(nameof(Points));
    public static readonly StyledProperty<Func<double, string>?> FormatProperty =
        AvaloniaProperty.Register<UsageChart, Func<double, string>?>(nameof(Format));
    /// <summary>강조할 막대 (기본 -1 = 마지막)</summary>
    public static readonly StyledProperty<int> HighlightIndexProperty =
        AvaloniaProperty.Register<UsageChart, int>(nameof(HighlightIndex), -1);
    public static readonly StyledProperty<Color> BarColorProperty =
        AvaloniaProperty.Register<UsageChart, Color>(nameof(BarColor), Color.FromUInt32(Palette.Lilac));
    public static readonly StyledProperty<Color> HighlightColorProperty =
        AvaloniaProperty.Register<UsageChart, Color>(nameof(HighlightColor), Color.FromUInt32(Palette.Butter));

    static UsageChart()
    {
        AffectsRender<UsageChart>(PointsProperty, FormatProperty, HighlightIndexProperty, BarColorProperty, HighlightColorProperty);
    }

    public IReadOnlyList<ChartPoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Func<double, string>? Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public int HighlightIndex { get => GetValue(HighlightIndexProperty); set => SetValue(HighlightIndexProperty, value); }
    public Color BarColor { get => GetValue(BarColorProperty); set => SetValue(BarColorProperty, value); }
    public Color HighlightColor { get => GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    /// <summary>도트 한 칸 (DIP)</summary>
    private const int Px = 2;
    private const int ChartHeight = 120;
    private const double FontSize = 12;
    private const int LabelBand = 16;

    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromUInt32(Palette.Outline));
    private static readonly IBrush DeepBrush = new SolidColorBrush(Color.FromUInt32(Palette.SpaceDeep));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.FromUInt32(Palette.PanelBorder));
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromUInt32(Palette.GrayDark));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromUInt32(Palette.Text));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromUInt32(Palette.TextDim));
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromUInt32(Palette.White));

    private int _hover = -1;

    public UsageChart()
    {
        Height = ChartHeight;
        ClipToBounds = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(HitBar(e.GetPosition(this).X));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        _hover = index;
        InvalidateVisual();
    }

    /// <summary>막대 배치: 왼쪽 끝, 막대 폭, 간격 (모두 도트 격자)</summary>
    private (int Left, int Bar, int Gap) Layout(int count)
    {
        int width = (int)Bounds.Width / Px * Px;
        int gap = count > 14 ? Px : Px * 2;
        int bar = Math.Max(Px, ((width - gap * (count - 1)) / count) / Px * Px);
        bar = Math.Min(bar, Px * 14);
        int used = bar * count + gap * (count - 1);
        int left = Math.Max(0, (width - used) / 2 / Px * Px);
        return (left, bar, gap);
    }

    private int HitBar(double x)
    {
        var pts = Points;
        if (pts is not { Count: > 0 }) return -1;
        var (left, bar, gap) = Layout(pts.Count);
        int i = (int)Math.Floor((x - left + gap / 2.0) / (bar + gap));
        return i >= 0 && i < pts.Count ? i : -1;
    }

    public override void Render(DrawingContext dc)
    {
        var pts = Points;
        if (pts is not { Count: > 0 } || Bounds.Width < Px * 8) return;

        var typeface = new Typeface(GetValue(TextElement.FontFamilyProperty));
        var format = Format ?? (v => v.ToString("0", CultureInfo.InvariantCulture));
        int n = pts.Count;
        int highlight = HighlightIndex is var h && h >= 0 && h < n ? h : n - 1;
        double max = 0;
        foreach (var p in pts) max = Math.Max(max, p.Value);

        var (left, bar, gap) = Layout(n);
        int top = LabelBand + Px * 2;
        int baseline = ((int)Bounds.Height - LabelBand - Px * 2) / Px * Px;
        int plot = baseline - top;
        int right = left + bar * n + gap * (n - 1);

        // 위 안내선 (최댓값 높이, 점선) + 최댓값 라벨
        string maxText = max > 0 ? format(max) : "0";
        var maxLabel = Label(maxText, typeface, DimBrush);
        dc.DrawText(maxLabel, new Point(left, 0));
        for (int x = left; x < right; x += Px * 3)
            dc.FillRectangle(GuideBrush, new Rect(x, top - Px, Px, Px));

        for (int i = 0; i < n; i++)
        {
            int x = left + i * (bar + gap);
            double v = pts[i].Value;
            bool lit = i == highlight;
            if (v <= 0 || max <= 0)
            {
                // 빈 날: 바닥 위 작은 점 한 줄
                dc.FillRectangle(EmptyBrush, new Rect(x + Px, baseline - Px, Math.Max(Px, bar - Px * 2), Px));
                continue;
            }
            int hgt = Math.Max(Px * 3, (int)Math.Round(v / max * plot / Px) * Px);
            var color = lit ? HighlightColor : BarColor;
            if (i == _hover) color = Lighten(color, 0.35);
            // 테두리 → 속 → 위쪽 반짝임 한 줄
            dc.FillRectangle(OutlineBrush, new Rect(x, baseline - hgt, bar, hgt));
            if (bar > Px * 2 && hgt > Px * 2)
            {
                dc.FillRectangle(BrushOf(color), new Rect(x + Px, baseline - hgt + Px, bar - Px * 2, hgt - Px));
                if (hgt > Px * 3)
                    dc.FillRectangle(BrushOf(Lighten(color, 0.45)), new Rect(x + Px, baseline - hgt + Px, bar - Px * 2, Px));
            }
            else
            {
                dc.FillRectangle(BrushOf(color), new Rect(x, baseline - hgt, bar, hgt));
            }
        }

        // 바닥 줄
        dc.FillRectangle(DeepBrush, new Rect(left - Px, baseline, right - left + Px * 2, Px));

        // 날짜 라벨: 오늘부터 왼쪽으로 일정 간격, 겹치면 건너뛴다
        int step = n <= 7 ? 1 : n <= 14 ? 2 : 7;
        double lastLeftEdge = double.MaxValue;
        for (int i = n - 1; i >= 0; i -= step)
        {
            var t = Label(pts[i].Label, typeface, i == highlight ? TextBrush : DimBrush);
            double cx = left + i * (bar + gap) + bar / 2.0;
            double tx = Math.Round(Math.Clamp(cx - t.Width / 2, 0, Math.Max(0, Bounds.Width - t.Width)));
            if (tx + t.Width + Px * 2 > lastLeftEdge) continue;
            dc.DrawText(t, new Point(tx, baseline + Px * 2));
            lastLeftEdge = tx;
        }

        // 마우스를 올린 막대: 오른쪽 위에 이름 · 값
        if (_hover >= 0 && _hover < n)
        {
            var p = pts[_hover];
            var t = Label($"{p.Detail ?? p.Label} · {format(p.Value)}", typeface, HoverBrush);
            double tx = Math.Max(left + maxLabel.Width + Px * 4, Math.Round(right - t.Width));
            dc.DrawText(t, new Point(tx, 0));
        }
    }

    /// <summary>막대 색 브러시 캐시 (색 종류가 몇 개뿐이라 그릴 때마다 만들지 않는다)</summary>
    private static readonly Dictionary<Color, IBrush> BrushCache = new();

    private static IBrush BrushOf(Color c)
    {
        if (!BrushCache.TryGetValue(c, out var b)) BrushCache[c] = b = new ImmutableSolidColorBrush(c);
        return b;
    }

    private static FormattedText Label(string text, Typeface typeface, IBrush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, brush);

    private static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
}
