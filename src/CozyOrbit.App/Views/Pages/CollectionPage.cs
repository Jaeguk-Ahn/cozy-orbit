using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>
/// 도감: 목적지(은하) 고르기 + 구역별 천체 칸 + 고른 천체의 설명(귀여운 한 줄 + 실제 사실).
/// 아직 가 보지 못한 목적지는 이름을 숨기고, 못 만난 천체는 흐리게 "???".
/// </summary>
public sealed class CollectionPage : Page
{
    private readonly StackPanel _destinations = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _grid = new() { Spacing = 6 };
    private readonly TextBlock _count = Text("", "h");
    private readonly PixelIcon _detailIcon = new() { Scale = 4, Animate = true, Width = 96, Height = 96 };
    private readonly TextBlock _detailName = Text("", "h");
    private readonly TextBlock _detailSector = Text("", "dim");
    private readonly TextBlock _detailNote = Text("");
    private readonly TextBlock _detailFact = Text("", "dim");
    private readonly TextBlock _detailFound = Text("", "dim");
    private readonly Dictionary<string, (Border Cell, PixelIcon Icon)> _cells = new();
    private int _shownDestination = -1;
    private int _viewDestination = -1;
    private int _shownCount = -1;
    private int _shownVoyage = -1;
    private string? _selected;

    public CollectionPage(GameHost host) : base(host)
    {
        var detailTexts = Column(3, _detailName, _detailSector, _detailNote, _detailFact, _detailFound);
        detailTexts.VerticalAlignment = VerticalAlignment.Center;
        var detailGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        Grid.SetColumn(detailTexts, 1);
        detailGrid.Children.Add(_detailIcon);
        detailGrid.Children.Add(detailTexts);

        Content = Column(6,
            new ScrollViewer { Content = _destinations, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            _count, Card(detailGrid), _grid);
    }

    public override void Tick()
    {
        var s = Host.State;
        if (s.DiscoveryCount != _shownCount || s.Voyage != _shownVoyage) Refresh();
    }

    public override void Refresh()
    {
        var s = Host.State;
        _shownCount = s.DiscoveryCount;
        if (_viewDestination < 0 || s.Voyage != _shownVoyage) _viewDestination = Math.Min(s.Voyage, Destinations.All.Count - 1);
        _shownVoyage = s.Voyage;
        BuildDestinationTabs(s);
        if (_shownDestination != _viewDestination) BuildGrid();
        UpdateCells(s);
    }

    /// <summary>목적지 아이콘 줄 — 가 본 곳과 지금 가는 곳만 고를 수 있다</summary>
    private void BuildDestinationTabs(GameState s)
    {
        _destinations.Children.Clear();
        for (int i = 0; i < Destinations.All.Count; i++)
        {
            var d = Destinations.All[i];
            bool reached = i <= s.Voyage;
            int index = i;
            var button = new Button
            {
                Padding = new Thickness(4),
                IsEnabled = reached,
                Content = new PixelIcon { Sprite = d.Sprite, Scale = 1, Dim = !reached },
                BorderBrush = i == _viewDestination ? Brush("StarlightBrush") : Brush("OutlineBrush"),
            };
            ToolTip.SetTip(button, reached ? d.Name : L.T("아직 가 보지 못한 곳", "Not reached yet"));
            button.Click += (_, _) =>
            {
                _viewDestination = index;
                _selected = null;
                Refresh();
            };
            _destinations.Children.Add(button);
        }
    }

    private void BuildGrid()
    {
        _shownDestination = _viewDestination;
        _grid.Children.Clear();
        _cells.Clear();
        var dest = Destinations.All[_viewDestination];
        foreach (var sector in dest.Sectors)
        {
            _grid.Children.Add(Header(L.T($"구역 {sector.Number} · {sector.Name}", $"Sector {sector.Number} · {sector.Name}"), "icon_distance"));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var d in dest.Catalog.Where(c => c.Sector == sector.Number))
            {
                var icon = new PixelIcon { Sprite = d.Sprite, Scale = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var cell = new Border
                {
                    Classes = { "card" },
                    Width = 58,
                    Height = 58,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 4, 4),
                    Child = icon,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                cell.PointerPressed += (_, _) =>
                {
                    _selected = d.Id;
                    UpdateCells(Host.State);
                };
                wrap.Children.Add(cell);
                _cells[d.Id] = (cell, icon);
            }
            _grid.Children.Add(wrap);
        }
    }

    private void UpdateCells(GameState s)
    {
        var dest = Destinations.All[_viewDestination];
        int found = dest.Catalog.Count(d => s.FoundAt.ContainsKey(d.Id));
        int nameless = _viewDestination == s.Voyage ? Math.Max(0, s.DiscoveryCount - dest.Catalog.Count) : 0;
        _count.Text = $"{dest.Name} · " + L.T($"도감 {found} / {dest.Catalog.Count}", $"Atlas {found} / {dest.Catalog.Count}")
                      + (nameless > 0 ? L.T($" · 이름 없는 별 {nameless}개", $" · {nameless} nameless stars") : "");

        foreach (var d in dest.Catalog)
        {
            var (cell, icon) = _cells[d.Id];
            icon.Dim = !s.FoundAt.ContainsKey(d.Id);
            cell.BorderBrush = d.Id == _selected ? Brush("StarlightBrush") : Brush("OutlineBrush");
        }

        // 고른 것이 없으면 이 목적지에서 가장 최근에 만난 천체
        _selected ??= dest.Catalog.Where(d => s.FoundAt.ContainsKey(d.Id)).OrderByDescending(d => s.FoundAt[d.Id]).Select(d => d.Id).FirstOrDefault()
                      ?? dest.Catalog[0].Id;
        if (_cells.TryGetValue(_selected, out var sel)) sel.Cell.BorderBrush = Brush("StarlightBrush");
        ShowDetail(dest, _selected, s);
    }

    private void ShowDetail(DestinationDef dest, string id, GameState s)
    {
        var d = dest.Catalog.FirstOrDefault(c => c.Id == id) ?? dest.Catalog[0];
        bool has = s.FoundAt.TryGetValue(d.Id, out var at);
        _detailIcon.Sprite = has ? d.Sprite : "icon_unknown";
        _detailName.Text = has ? d.Name : "???";
        var sector = dest.Sectors[d.Sector - 1];
        _detailSector.Text = L.T($"{dest.Name} · 구역 {sector.Number} {sector.Name}", $"{dest.Name} · Sector {sector.Number} {sector.Name}");
        _detailNote.Text = has ? d.Note : L.T("아직 만나지 못했어요. 계속 날아가 봐요!", "Not found yet. Keep flying!");
        _detailFact.Text = has && d.Fact is { } fact ? L.T("실제로는: ", "In reality: ") + fact : "";
        _detailFact.IsVisible = _detailFact.Text.Length > 0;
        _detailFound.Text = has ? L.T($"처음 만난 날 {at.ToLocalTime():yyyy-MM-dd HH:mm}", $"First seen {at.ToLocalTime():yyyy-MM-dd HH:mm}") : "";
    }
}
