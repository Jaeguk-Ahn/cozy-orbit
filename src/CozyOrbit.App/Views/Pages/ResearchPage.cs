using Avalonia.Controls;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>
/// 연구(업그레이드) 목록 + 맨 위 항해 카드 (지금 목적지·도감 진행·새 항해 떠나기).
/// 행은 한 번 만들고 진행마다 글자·버튼 상태만 고친다.
/// </summary>
public sealed class ResearchPage : Page
{
    private sealed record RowParts(UpgradeDef Def, Border Card, TextBlock Title, TextBlock Effect, TextBlock Eta, Button Buy, TextBlock Cost);

    private readonly List<RowParts> _rows = new();
    private readonly PixelIcon _voyageIcon = new() { Scale = 2, Animate = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _voyageTitle = Text("", "h");
    private readonly TextBlock _voyageInfo = Text("", "dim");
    private readonly TextBlock _voyageHint = Text("", "dim");
    private readonly TextBlock _sectorInfo = Text("", "dim");
    /// <summary>테두리로 살짝 표시한 행 — 가장 싼 연구 (지금 수입이면 가장 먼저 살 수 있는 것)</summary>
    private RowParts? _highlighted;
    private readonly Button _depart = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "accent" } };
    /// <summary>한 번 누르면 "정말 떠날까요?" — 두 번째에 떠난다 (연구·별가루가 처음부터라서)</summary>
    private bool _confirming;

    public ResearchPage(GameHost host) : base(host)
    {
        _depart.Click += (_, _) =>
        {
            if (!_confirming)
            {
                _confirming = true;
                Tick();
                return;
            }
            _confirming = false;
            Host.Depart();
        };
        var voyageTexts = Column(2, _voyageTitle, _voyageInfo, _sectorInfo);
        voyageTexts.VerticalAlignment = VerticalAlignment.Center;
        var voyageGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        Grid.SetColumn(voyageTexts, 1);
        voyageGrid.Children.Add(_voyageIcon);
        voyageGrid.Children.Add(voyageTexts);

        var list = Column(6, Card(Column(6, voyageGrid, _voyageHint, _depart)),
            Text(L.T("별가루로 도토리호와 연구소를 키워요.", "Spend stardust to upgrade the Acorn and the lab."), "dim"));
        foreach (var u in Upgrades.All)
        {
            var title = Text("");
            var effect = Text("", "dim");
            var eta = Text("", "dim");
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
            var texts = Column(2, title, effect, eta);
            texts.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(buy, 2);
            grid.Children.Add(icon);
            grid.Children.Add(texts);
            grid.Children.Add(buy);
            var card = Card(grid);
            list.Children.Add(card);
            _rows.Add(new RowParts(u, card, title, effect, eta, buy, cost));
        }
        Content = list;
    }

    public override void Refresh() => Tick();

    public override void Tick()
    {
        var s = Host.State;
        UpdateVoyage(s);
        double rate = GameRules.DustPerSecond(s);
        RowParts? cheapest = null;
        double cheapestCost = double.PositiveInfinity;
        foreach (var r in _rows)
        {
            int level = s.Level(r.Def.Id);
            bool maxed = r.Def.IsMaxed(level);
            double cost = r.Def.CostAt(level);
            if (!maxed && cost < cheapestCost)
            {
                cheapest = r;
                cheapestCost = cost;
            }
            Set(r.Title, $"{r.Def.Name}  Lv.{level}");
            Set(r.Effect, maxed ? $"{r.Def.Effect} {r.Def.Value(level)}" + L.T("  (최대)", "  (max)")
                                : $"{r.Def.Effect} {r.Def.Value(level)} → {r.Def.Value(level + 1)}");
            Set(r.Cost, maxed ? L.T("최대", "Max") : Fmt.Compact(cost));
            r.Buy.IsEnabled = GameRules.CanBuy(s, r.Def);
            // 모자란 별가루를 지금 수입(부스트 포함)으로 모으는 데 걸리는 시간
            bool waiting = !maxed && cost > s.Stardust && rate > 0;
            r.Eta.IsVisible = waiting;
            if (waiting)
            {
                string span = Fmt.Duration(Math.Max(1, (cost - s.Stardust) / rate));
                Set(r.Eta, L.T($"약 {span} 뒤", $"in about {span}"));
            }
        }
        if (!ReferenceEquals(cheapest, _highlighted))
        {
            _highlighted?.Card.ClearValue(Border.BorderBrushProperty);
            if (cheapest != null) cheapest.Card.BorderBrush = Brush("StarlightBrush");
            _highlighted = cheapest;
        }
    }

    private void UpdateVoyage(GameState s)
    {
        var dest = Destinations.Current(s);
        int goal = Destinations.GoalCount(dest);
        bool ready = Voyages.CanDepart(s);
        if (_voyageIcon.Sprite != dest.Sprite) _voyageIcon.Sprite = dest.Sprite;
        Set(_voyageTitle, L.T($"항해 {s.Voyage + 1} · {dest.Name}", $"Voyage {s.Voyage + 1} · {dest.Name}"));
        Set(_voyageInfo, L.T($"도감 {Math.Min(s.DiscoveryCount, goal)} / {goal} · 항해 배율 ×{Fmt.Number(GameRules.VoyageMultiplier(s), 2)}",
                             $"Atlas {Math.Min(s.DiscoveryCount, goal)} / {goal} · voyage bonus ×{Fmt.Number(GameRules.VoyageMultiplier(s), 2)}"));
        _depart.IsVisible = ready;
        UpdateSector(s, dest, ready);
        if (!ready)
        {
            _confirming = false;
            Set(_voyageHint, L.T("이 목적지의 천체를 모두 만나면 다음 목적지로 새 항해를 떠날 수 있어요.",
                                 "Meet every object here to set off on a new voyage to the next destination."));
            return;
        }
        var next = Destinations.For(s.Voyage + 1);
        Set(_voyageHint, L.T(
            $"도감 완성! {next.Name}(으)로 떠나면 거리·별가루·연구가 처음부터지만, 속도·별가루가 영구히 ×{Fmt.Number(Voyages.NextMultiplier(s), 2)} 이 되고 별사탕 +{Fmt.Compact(Voyages.DepartureCandy(s))} 을 받아요. 별빛·별사탕·스킨·소품·도감은 그대로예요.",
            $"Atlas complete! Leaving for {next.Name} resets distance, stardust and research, but speed and stardust become ×{Fmt.Number(Voyages.NextMultiplier(s), 2)} for good and you get +{Fmt.Compact(Voyages.DepartureCandy(s))} star candy. Starlight, candy, skins, decor and the atlas stay."));
        string label = _confirming ? L.T("정말 떠날까요? 한 번 더 누르면 출발!", "Really go? Press again to launch!") : L.T($"새 항해 떠나기 → {next.Name}", $"New voyage → {next.Name}");
        if (_depart.Content as string != label) _depart.Content = label;
    }

    /// <summary>지금 구역의 진행 + 다 채우면 받을 보상 (도감 밖·끝없는 우주·도감 완성 뒤엔 숨김)</summary>
    private void UpdateSector(GameState s, DestinationDef dest, bool ready)
    {
        bool show = !ready && s.DiscoveryCount < dest.Catalog.Count;
        _sectorInfo.IsVisible = show;
        if (!show) return;
        var sector = Destinations.SectorOf(dest, s.DiscoveryCount);
        int found = Sectors.FoundThisVoyage(s, sector.Number);
        int size = Sectors.Size(dest, sector.Number);
        string dust = Fmt.Compact(Sectors.StardustReward(s, sector.Number));
        string candy = Fmt.Compact(Balance.SectorCandy);
        Set(_sectorInfo, L.T($"{sector.Name} {found} / {size} · 다 채우면 별가루 +{dust} · 별사탕 +{candy}",
                             $"{sector.Name} {found} / {size} · complete it for +{dust} stardust · +{candy} candy"));
    }

    private static void Set(TextBlock t, string text)
    {
        if (t.Text != text) t.Text = text;
    }
}
