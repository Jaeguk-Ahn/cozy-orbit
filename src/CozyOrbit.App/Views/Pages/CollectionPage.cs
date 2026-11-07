using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>도감: 구역별 천체 칸 + 고른 천체의 설명. 아직 못 만난 천체는 흐리게 "???".</summary>
public sealed class CollectionPage : Page
{
    private readonly Dictionary<string, (Border Cell, PixelIcon Icon)> _cells = new();
    private readonly TextBlock _count = Text("", "h");
    private readonly PixelIcon _detailIcon = new() { Scale = 4, Animate = true, Width = 96, Height = 96 };
    private readonly TextBlock _detailName = Text("", "h");
    private readonly TextBlock _detailSector = Text("", "dim");
    private readonly TextBlock _detailNote = Text("");
    private readonly TextBlock _detailFound = Text("", "dim");
    private string? _selected;
    private int _shownCount = -1;

    public CollectionPage(GameHost host) : base(host)
    {
        var detailTexts = Column(3, _detailName, _detailSector, _detailNote, _detailFound);
        detailTexts.VerticalAlignment = VerticalAlignment.Center;
        var detailGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        Grid.SetColumn(detailTexts, 1);
        detailGrid.Children.Add(_detailIcon);
        detailGrid.Children.Add(detailTexts);

        var root = Column(6, _count, Card(detailGrid));
        foreach (var sector in Discoveries.Sectors)
        {
            root.Children.Add(Header(L.T($"구역 {sector.Number} · {sector.Name}", $"Sector {sector.Number} · {sector.Name}"), "icon_distance"));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var d in Discoveries.Catalog.Where(c => c.Sector == sector.Number))
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
                cell.PointerPressed += (_, _) => Select(d.Id);
                wrap.Children.Add(cell);
                _cells[d.Id] = (cell, icon);
            }
            root.Children.Add(wrap);
        }
        Content = root;
    }

    public override void Tick()
    {
        if (Host.State.DiscoveryCount != _shownCount) Refresh();
    }

    public override void Refresh()
    {
        var s = Host.State;
        _shownCount = s.DiscoveryCount;
        int found = Discoveries.Catalog.Count(d => s.FoundAt.ContainsKey(d.Id));
        int nameless = Math.Max(0, s.DiscoveryCount - Discoveries.Catalog.Count);
        _count.Text = L.T($"도감 {found} / {Discoveries.Catalog.Count}", $"Atlas {found} / {Discoveries.Catalog.Count}")
                      + (nameless > 0 ? L.T($"  · 이름 없는 별 {nameless}개", $"  · {nameless} nameless stars") : "");

        foreach (var d in Discoveries.Catalog)
        {
            var (cell, icon) = _cells[d.Id];
            bool has = s.FoundAt.ContainsKey(d.Id);
            icon.Dim = !has;
            cell.BorderBrush = d.Id == _selected ? Brush("StarlightBrush") : Brush("OutlineBrush");
        }

        // 고른 것이 없으면 가장 최근에 만난 천체
        _selected ??= s.FoundAt.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? Discoveries.Catalog[0].Id;
        ShowDetail(_selected);
    }

    private void Select(string id)
    {
        _selected = id;
        Refresh();
    }

    private void ShowDetail(string id)
    {
        var d = Discoveries.Catalog.First(c => c.Id == id);
        var s = Host.State;
        bool has = s.FoundAt.TryGetValue(id, out var at);
        _detailIcon.Sprite = has ? d.Sprite : "icon_unknown";
        _detailName.Text = has ? d.Name : "???";
        var sector = Discoveries.Sectors[d.Sector - 1];
        _detailSector.Text = L.T($"구역 {sector.Number} · {sector.Name}", $"Sector {sector.Number} · {sector.Name}");
        _detailNote.Text = has ? d.Note : L.T("아직 만나지 못했어요. 계속 날아가 봐요!", "Not found yet. Keep flying!");
        _detailFound.Text = has ? L.T($"처음 만난 날 {at.ToLocalTime():yyyy-MM-dd HH:mm}", $"First seen {at.ToLocalTime():yyyy-MM-dd HH:mm}") : "";
    }
}
