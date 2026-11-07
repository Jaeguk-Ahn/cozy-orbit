using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Art;

namespace CozyOrbit.App.Views.Scene;

/// <summary>
/// 장면 위의 말풍선 (한 번에 하나). 알림(발견·별사탕 등)은 줄 서서 차례로, 햄찌를 쓰다듬은 반응은 바로 끼어든다.
/// 위치: 햄찌 번호가 있으면 그 햄찌 머리 위, 아니면 지정한 점 위. 피할 칸(탐사선)과 겹치면 옆으로 비켜서고, 자리가 모자라면 폭을 줄인다.
/// </summary>
public sealed class SceneBubbles
{
    public sealed class Bubble
    {
        public required string Text { get; init; }
        public string? Icon { get; init; }
        /// <summary>이 햄찌 머리 위 (null 이면 At)</summary>
        public int? Hamster { get; init; }
        public Point At { get; init; }
        public double Duration { get; init; } = 3.5;
        public double Age { get; set; }
    }

    private const int MaxQueue = 3;
    private const double FadeSeconds = 0.3;

    private static readonly Typeface Font = new(new FontFamily("avares://CozyOrbit/Assets/Fonts#Galmuri11"));
    private readonly Queue<Bubble> _queue = new();

    public Bubble? Current { get; private set; }

    /// <summary>알림 말풍선: 줄 서서 차례로 (오래된 것부터 버린다)</summary>
    public void Enqueue(Bubble b)
    {
        while (_queue.Count >= MaxQueue) _queue.Dequeue();
        _queue.Enqueue(b);
        Current ??= _queue.Dequeue();
    }

    /// <summary>바로 보여 주기 (쓰다듬기·별똥별 반응)</summary>
    public void Interrupt(Bubble b) => Current = b;

    public void Update(double dt)
    {
        if (Current == null) return;
        Current.Age += dt;
        if (Current.Age >= Current.Duration) Current = _queue.Count > 0 ? _queue.Dequeue() : null;
    }

    /// <summary>현재 말풍선을 그린다. anchor = 꼬리 끝이 닿을 점 (px), avoid = 가리면 안 되는 칸 (비어 있으면 무시)</summary>
    public void Draw(DrawingContext dc, Point anchor, double width, int scale, Rect avoid = default)
    {
        if (Current is not { } b) return;
        double fadeIn = Math.Min(1, b.Age / FadeSeconds);
        double fadeOut = Math.Min(1, (b.Duration - b.Age) / FadeSeconds);
        double opacity = Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
        if (opacity <= 0) return;

        const double pad = 5, border = 2, tail = 6;
        double iconW = 0, iconH = 0;
        if (b.Icon != null && SpriteLibrary.TryGet(b.Icon, out var icon))
        {
            int iconScale = icon.Width > 16 ? 1 : 2;
            if (icon.Width * iconScale > 24) iconScale = 1;
            iconW = icon.Width * iconScale + 4;
            iconH = icon.Height * iconScale;
        }

        var text = Layout(b.Text, Math.Max(60, Math.Min(200, width - 40)));
        double w = Math.Ceiling(text.Width + iconW + pad * 2);
        double h = Math.Ceiling(Math.Max(text.Height, iconH) + pad * 2);
        double x = Math.Round(Math.Clamp(anchor.X - w / 2, 2, Math.Max(2, width - w - 2)));
        double y = Math.Round(Math.Max(2, anchor.Y - tail - h));

        if (avoid.Width > 0 && new Rect(x, y, w, h + tail).Intersects(avoid))
        {
            // 탐사선 오른쪽 → 왼쪽 순으로 비켜선다. 둘 다 모자라면 오른쪽 자리에 맞게 글 폭을 줄여 다시 잰다.
            double right = avoid.Right + 4, left = avoid.Left - 4;
            if (right + w <= width - 2) x = Math.Round(right);
            else if (left - w >= 2) x = Math.Round(left - w);
            else if (width - 2 - right - iconW - pad * 2 >= 60)
            {
                text = Layout(b.Text, width - 2 - right - iconW - pad * 2);
                w = Math.Ceiling(text.Width + iconW + pad * 2);
                h = Math.Ceiling(Math.Max(text.Height, iconH) + pad * 2);
                x = Math.Round(right);
                y = Math.Round(Math.Max(2, anchor.Y - tail - h));
            }
            else if (avoid.Bottom + 2 + h <= anchor.Y - tail)
            {
                // 아주 좁으면 탐사선 아래로 (꼬리는 그대로 아래를 가리킨다)
                y = Math.Round(avoid.Bottom + 2);
            }
        }

        using (dc.PushOpacity(opacity))
        {
            var outline = SkinArt.Solid(Palette.Outline);
            var fill = SkinArt.Solid(Palette.White);
            // 네모 모서리를 한 칸씩 깎아 도트 느낌의 둥근 말풍선
            dc.FillRectangle(outline, new Rect(x + border, y, w - border * 2, h));
            dc.FillRectangle(outline, new Rect(x, y + border, w, h - border * 2));
            dc.FillRectangle(fill, new Rect(x + border, y + border, w - border * 2, h - border * 2));
            // 꼬리: 2px 계단 세 줄 (윗줄은 말풍선 아래 테두리를 덮어 입구를 연다)
            double tx = Math.Round(Math.Clamp(anchor.X, x + 10, x + w - 10));
            double ty = y + h - border;
            dc.FillRectangle(outline, new Rect(tx - 6, ty, 12, 2));
            dc.FillRectangle(fill, new Rect(tx - 4, ty, 8, 2));
            dc.FillRectangle(outline, new Rect(tx - 4, ty + 2, 8, 2));
            dc.FillRectangle(fill, new Rect(tx - 2, ty + 2, 4, 2));
            dc.FillRectangle(outline, new Rect(tx - 2, ty + 4, 4, 2));

            double cx = x + pad;
            if (b.Icon != null && iconW > 0)
            {
                var sprite = SpriteLibrary.Get(b.Icon);
                double s = (iconW - 4) / sprite.Width;
                SpriteCache.Draw(dc, b.Icon, SpriteCache.FrameAt(b.Icon, b.Age), Math.Round(cx), Math.Round(y + (h - iconH) / 2), s);
                cx += iconW;
            }
            dc.DrawText(text, new Point(Math.Round(cx), Math.Round(y + (h - text.Height) / 2)));
        }
    }

    private static FormattedText Layout(string s, double maxWidth) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 12, SkinArt.Solid(Palette.Outline)) { MaxTextWidth = maxWidth };
}
