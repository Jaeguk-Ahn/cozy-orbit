using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>스킨 상점: 별사탕 지갑 + 자리별 스킨 칸 (미리보기 · 이름 · 사기/장착 버튼).</summary>
public sealed class ShopPage : Page
{
    private sealed record Item(SkinDef Skin, SkinPreview Preview, Border Card, Button Action, TextBlock ActionText);

    private readonly List<Item> _items = new();
    private readonly TextBlock _balance = new() { Classes = { "big" } };
    private readonly TextBlock _session = Text("", "dim");

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
        Content = root;
    }

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
                SetContent(item, L.T("장착 중", "Equipped"));
                item.Action.IsEnabled = false;
            }
            else if (owned)
            {
                SetContent(item, L.T("장착", "Equip"));
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
    }

    private static void SetContent(Item item, string text)
    {
        if (item.Action.Content as string != text) item.Action.Content = text;
    }

    private static void Set(TextBlock t, string text)
    {
        if (t.Text != text) t.Text = text;
    }
}
