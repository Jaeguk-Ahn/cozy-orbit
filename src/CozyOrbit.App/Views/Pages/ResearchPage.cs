using Avalonia.Controls;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>연구(업그레이드) 목록. 행은 한 번 만들고 진행마다 글자·버튼 상태만 고친다.</summary>
public sealed class ResearchPage : Page
{
    private sealed record RowParts(UpgradeDef Def, TextBlock Title, TextBlock Effect, Button Buy, TextBlock Cost);

    private readonly List<RowParts> _rows = new();

    public ResearchPage(GameHost host) : base(host)
    {
        var list = Column(6, Text(L.T("별가루로 도토리호와 연구소를 키워요.", "Spend stardust to upgrade the Acorn and the lab."), "dim"));
        foreach (var u in Upgrades.All)
        {
            var title = Text("");
            var effect = Text("", "dim");
            var cost = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var buy = new Button
            {
                MinWidth = 92,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Content = Row(4, new PixelIcon { Sprite = "icon_stardust", Scale = 1, VerticalAlignment = VerticalAlignment.Center }, cost),
            };
            buy.Click += (_, _) => Host.Buy(u.Id);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
            var icon = new PixelIcon { Sprite = u.Icon, Scale = 3, VerticalAlignment = VerticalAlignment.Center };
            var texts = Column(2, title, effect);
            texts.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(buy, 2);
            grid.Children.Add(icon);
            grid.Children.Add(texts);
            grid.Children.Add(buy);
            list.Children.Add(Card(grid));
            _rows.Add(new RowParts(u, title, effect, buy, cost));
        }
        Content = list;
    }

    public override void Refresh() => Tick();

    public override void Tick()
    {
        var s = Host.State;
        foreach (var r in _rows)
        {
            int level = s.Level(r.Def.Id);
            bool maxed = r.Def.IsMaxed(level);
            Set(r.Title, $"{r.Def.Name}  Lv.{level}");
            Set(r.Effect, maxed ? $"{r.Def.Effect} {r.Def.Value(level)}" + L.T("  (최대)", "  (max)")
                                : $"{r.Def.Effect} {r.Def.Value(level)} → {r.Def.Value(level + 1)}");
            Set(r.Cost, maxed ? L.T("최대", "Max") : Fmt.Compact(r.Def.CostAt(level)));
            r.Buy.IsEnabled = GameRules.CanBuy(s, r.Def);
        }
    }

    private static void Set(TextBlock t, string text)
    {
        if (t.Text != text) t.Text = text;
    }
}
