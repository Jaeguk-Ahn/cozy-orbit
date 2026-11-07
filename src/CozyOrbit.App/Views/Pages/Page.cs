using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CozyOrbit.App.Views.Pages;

/// <summary>탭 한 장. Tick 은 진행마다(가볍게), Refresh 는 내용이 바뀌었을 때(목록 다시 만들기).</summary>
public abstract class Page : UserControl
{
    protected GameHost Host { get; }

    protected Page(GameHost host) => Host = host;

    public virtual void Tick() { }
    public virtual void Refresh() { }

    // ── 작은 UI 도우미 ──

    protected static TextBlock Text(string text, string? cls = null, IBrush? fg = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        if (cls != null) t.Classes.Add(cls);
        if (fg != null) t.Foreground = fg;
        return t;
    }

    /// <summary>
    /// 색 리소스. 화면에 붙지 않은 페이지(생성자 안, 캐시된 숨은 탭)는 컨트롤 트리로는 못 찾으므로
    /// 앱 리소스(App.axaml.cs 에서 넣은 팔레트 브러시)에서 찾는다 — 예전엔 여기서 InvalidCastException 으로 앱이 죽었다.
    /// </summary>
    protected IBrush Brush(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b) return b;
        var app = Application.Current;
        if (app != null && app.TryFindResource(key, app.ActualThemeVariant, out v) && v is IBrush ab) return ab;
        return Brushes.Transparent;
    }

    protected static Border Card(Control child) => new() { Classes = { "card" }, Child = child };

    protected static StackPanel Row(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    protected static StackPanel Column(double spacing, params Control[] children)
    {
        var p = new StackPanel { Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    protected static Control Header(string text, string icon)
    {
        var row = Row(6, new PixelIcon { Sprite = icon, Scale = 1, VerticalAlignment = VerticalAlignment.Center }, Text(text, "h"));
        row.Margin = new Thickness(0, 6, 0, 2);
        return row;
    }
}
