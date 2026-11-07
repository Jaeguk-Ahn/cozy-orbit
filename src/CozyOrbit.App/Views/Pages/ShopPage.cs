using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>상점: 별사탕 지갑 + 자리별 스킨 칸 (미리보기 · 이름 · 사기/장착 버튼) + 연구소 꾸미기 소품 칸 (사기/놓기/치우기).</summary>
public sealed class ShopPage : Page
{
    private sealed record Item(SkinDef Skin, SkinPreview Preview, Border Card, Button Action, TextBlock ActionText);
    private sealed record DecorItem(DecorDef Def, Border Card, Button Action, TextBlock ActionText);

    private readonly List<Item> _items = new();
    private readonly List<DecorItem> _decor = new();
    private readonly TextBlock _balance = new() { Classes = { "big" } };
    private readonly TextBlock _session = Text("", "dim");
    private readonly TextBlock _decorSummary = Text("");

    public ShopPage(GameHost host) : base(host)
    {
        var root = Column(6);
        root.Children.Add(Card(Column(4,
            Row(6, new PixelIcon { Sprite = "icon_candy", Scale = 2, VerticalAlignment = VerticalAlignment.Center }, Text(L.T("별사탕", "Star Candy"), "h")),
            _balance,
            Text(L.T($"게임을 켜 둔 동안 쓴 AI 토큰이 별사탕이 돼요. 가중 토큰 {Fmt.Compact(CandyLedger.WeightedPerCandy)}개마다 1개 (꺼 둔 동안의 사용량은 별빛만 돼요).",
                     $"AI tokens used while the game is running become star candy: 1 per {Fmt.Compact(CandyLedger.WeightedPerCandy)} weighted tokens (usage while closed only makes starlight)."), "dim"),
            _session)));

        foreach (var slot in Enum.GetValues<SkinSlot>())
        {
            root.Children.Add(Header(Skins.SlotName(slot), SlotIcon(slot)));
            var wrap = new WrapPanel();
            foreach (var skin in Skins.All.Where(x => x.Slot == slot))
            {
                var preview = new SkinPreview { Skin = skin, HorizontalAlignment = HorizontalAlignment.Center };
                var actionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                var action = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4, 3) };
                action.Click += (_, _) => OnAction(skin);
                var name = new TextBlock { Text = skin.Name, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var card = new Border
                {
                    Classes = { "card" },
                    Width = 124,
                    Margin = new Thickness(0, 0, 4, 4),
                    Child = Column(4, preview, name, action),
                };
                ToolTip.SetTip(card, skin.Note);
                wrap.Children.Add(card);
                _items.Add(new Item(skin, preview, card, action, actionText));
            }
            root.Children.Add(wrap);
        }

        root.Children.Add(Header(L.T("연구소 꾸미기", "Lab Decor"), "icon_decor"));
        root.Children.Add(Card(Column(4,
            _decorSummary,
            Text(L.T($"별사탕으로 사거나 천체를 발견해 기념품으로 얻은 소품을 연구소 벽과 바닥에 {Decor.MaxPerKind}개씩 놓을 수 있어요.",
                     $"Buy decor with star candy or get souvenirs from discoveries, then place up to {Decor.MaxPerKind} each on the lab wall and floor."), "dim"))));
        foreach (var kind in new[] { DecorKind.Wall, DecorKind.Floor })
        {
            root.Children.Add(Text(KindName(kind), "dim"));
            var wrap = new WrapPanel();
            foreach (var d in Decor.All.Where(x => x.Kind == kind))
            {
                var icon = new PixelIcon
                {
                    Sprite = d.Sprite, Scale = 3, Animate = true, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = kind == DecorKind.Floor ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                };
                // 소품 최대 18×18 → 3배 54px. 바닥 소품은 바닥에 맞춰 세운다
                var preview = new Border { Height = 54, Child = icon };
                var actionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
                var action = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4, 3) };
                action.Click += (_, _) => OnDecorAction(d);
                var name = new TextBlock { Text = d.Name, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var card = new Border
                {
                    Classes = { "card" },
                    Width = 124,
                    Margin = new Thickness(0, 0, 4, 4),
                    Child = Column(4, preview, name, action),
                };
                ToolTip.SetTip(card, d.Name);
                wrap.Children.Add(card);
                _decor.Add(new DecorItem(d, card, action, actionText));
            }
            root.Children.Add(wrap);
        }
        Content = root;
    }

    private static string KindName(DecorKind kind) => kind == DecorKind.Wall ? L.T("벽에 거는 소품", "Wall decor") : L.T("바닥에 두는 소품", "Floor decor");

    private static string SlotIcon(SkinSlot slot) => slot switch
    {
        SkinSlot.Outfit or SkinSlot.Fur => "icon_crew",
        SkinSlot.Ship => "icon_engine",
        _ => "icon_starlight",
    };

    private void OnAction(SkinDef skin)
    {
        if (Shop.Owns(Host.State, skin.Id)) Host.EquipSkin(skin.Id);
        else Host.BuySkin(skin.Id);
        Refresh();
    }

    /// <summary>놓였으면 치우고, 가졌으면 놓고, 아니면 산다</summary>
    private void OnDecorAction(DecorDef d)
    {
        var s = Host.State;
        bool changed;
        if (Decor.IsPlaced(s, d.Id)) changed = Decor.Remove(s, d.Id);
        else if (Decor.Owns(s, d.Id)) changed = Decor.Place(s, d.Id);
        else
        {
            changed = Decor.TryBuy(s, d.Id);
            if (changed)
            {
                AppLog.Info($"소품 구매 {d.Id}");
                Infra.Sound.Play(CozyOrbit.Core.Audio.SoundId.Buy);
            }
        }
        if (!changed) return;
        Host.Save();
        Refresh();
    }

    public override void Refresh() => Tick();

    public override void Tick()
    {
        var s = Host.State;
        _balance.Foreground ??= Brush("StardustBrush");
        Set(_balance, Fmt.Compact(Math.Floor(s.Candy.Balance)));
        var session = s.Candy.Sessions.LastOrDefault();
        Set(_session, session == null ? "" : L.T(
            $"지금 켜 둔 지 {Fmt.Duration((session.End - session.Start).TotalSeconds)} · 그동안 받은 별사탕 {Fmt.Number(session.CreditedWeighted / CandyLedger.WeightedPerCandy, 1)}",
            $"Running for {Fmt.Duration((session.End - session.Start).TotalSeconds)} · candy earned this run {Fmt.Number(session.CreditedWeighted / CandyLedger.WeightedPerCandy, 1)}"));

        string captainFur = SkinArt.CaptainFur(s);
        foreach (var item in _items)
        {
            item.Preview.CaptainFur = captainFur;
            bool owned = Shop.Owns(s, item.Skin.Id);
            bool equipped = Shop.Equipped(s, item.Skin.Slot) == item.Skin.Id;
            item.Card.BorderBrush = equipped ? Brush("StarlightBrush") : Brush("OutlineBrush");
            if (equipped)
            {
                SetContent(item.Action, L.T("장착 중", "Equipped"));
                item.Action.IsEnabled = false;
            }
            else if (owned)
            {
                SetContent(item.Action, L.T("장착", "Equip"));
                item.Action.IsEnabled = true;
            }
            else
            {
                Set(item.ActionText, Fmt.Compact(item.Skin.Price));
                if (item.Action.Content is not StackPanel)
                    item.Action.Content = Row(4, new PixelIcon { Sprite = "icon_candy", Scale = 1, VerticalAlignment = VerticalAlignment.Center }, item.ActionText);
                item.Action.IsEnabled = Shop.CanBuy(s, item.Skin);
            }
        }

        TickDecor(s);
    }

    private void TickDecor(GameState s)
    {
        var wall = Decor.Placed(s, DecorKind.Wall);
        var floor = Decor.Placed(s, DecorKind.Floor);
        Set(_decorSummary, L.T($"놓은 소품: 벽 {wall.Count}/{Decor.MaxPerKind} · 바닥 {floor.Count}/{Decor.MaxPerKind}",
                               $"Placed: wall {wall.Count}/{Decor.MaxPerKind} · floor {floor.Count}/{Decor.MaxPerKind}"));
        foreach (var item in _decor)
        {
            var d = item.Def;
            var placedOfKind = d.Kind == DecorKind.Wall ? wall : floor;
            bool placed = placedOfKind.Contains(d);
            item.Card.BorderBrush = placed ? Brush("StarlightBrush") : Brush("OutlineBrush");
            if (placed)
            {
                SetContent(item.Action, L.T("치우기", "Remove"));
                item.Action.IsEnabled = true;
            }
            else if (Decor.Owns(s, d.Id))
            {
                bool room = placedOfKind.Count < Decor.MaxPerKind;
                SetContent(item.Action, room ? L.T("놓기", "Place") : L.T("자리가 꽉 찼어요", "No room"));
                item.Action.IsEnabled = room;
            }
            else if (d.SouvenirOf is { } discoveryId)
            {
                // 못 만난 천체 이름은 숨긴다
                string name = s.FoundAt.ContainsKey(discoveryId) && Destinations.Find(discoveryId) is { } found ? found.Discovery.Name : "???";
                Set(item.ActionText, L.T($"발견하면 얻어요:\n{name}", $"Discover to get:\n{name}"));
                if (item.Action.Content != item.ActionText) item.Action.Content = item.ActionText;
                item.Action.IsEnabled = false;
            }
            else
            {
                Set(item.ActionText, Fmt.Compact(d.Price));
                if (item.Action.Content is not StackPanel)
                    item.Action.Content = Row(4, new PixelIcon { Sprite = "icon_candy", Scale = 1, VerticalAlignment = VerticalAlignment.Center }, item.ActionText);
                item.Action.IsEnabled = Decor.CanBuy(s, d.Id);
            }
        }
    }

    private static void SetContent(Button button, string text)
    {
        if (button.Content as string != text) button.Content = text;
    }

    private static void Set(TextBlock t, string text)
    {
        if (t.Text != text) t.Text = text;
    }
}
