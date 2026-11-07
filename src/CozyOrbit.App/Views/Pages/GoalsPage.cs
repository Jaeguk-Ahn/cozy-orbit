using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>
/// 오늘의 목표 세 가지 + 덤, 그 아래 도전과제 목록 (받을 수 있는 것 → 진행 중 → 받은 것).
/// 행은 한 번 만들고 Tick 에서는 글자·막대·버튼만 고친다. 목표 행은 날이 바뀔 때만 다시 만든다.
/// </summary>
public sealed class GoalsPage : Page
{
    /// <summary>오른쪽 칸: 받기 버튼 / 보상 표시 / 끝난 표시 중 하나만 보인다</summary>
    private sealed record ClaimCell(Button Claim, StackPanel Reward, TextBlock Done);

    private sealed record GoalRow(DailyGoal Goal, GoalDef Def, Border Card, ProgressBar Bar, TextBlock Amount, ClaimCell Cell);

    private sealed record AchievementRow(AchievementDef Def, Border Card, ProgressBar Bar, TextBlock Amount, ClaimCell Cell);

    private readonly StackPanel _goalList = Column(6);
    private readonly List<GoalRow> _goals = new();

    private readonly Border _bonusCard;
    private readonly TextBlock _bonusAmount = Text("", "dim");
    private readonly ClaimCell _bonusCell;

    private readonly TextBlock _achievementSummary = Text("", "dim");
    private readonly StackPanel _achievementList = Column(6);
    private readonly List<AchievementRow> _achievements = new();
    /// <summary>도전과제별 지금 묶음 (0 받을 수 있음 · 1 진행 중 · 2 받음, -1 아직 모름) — 바뀔 때만 목록 순서를 다시 꽂는다</summary>
    private readonly int[] _groups;

    private IBrush? _starlight, _good, _outline, _dim;

    public GoalsPage(GameHost host) : base(host)
    {
        string tomorrow = L.T("내일 다시", "See you tomorrow");
        _bonusCell = MakeCell(DailyGoals.BonusReward, () => DailyGoals.TryClaimBonus(Host.State), tomorrow);
        _bonusCard = Card(Columns(
            Icon("icon_candy"),
            Column(2,
                Text(L.T("세 가지를 모두 받으면 덤", "Bonus for claiming all three")),
                _bonusAmount),
            CellControl(_bonusCell)));

        foreach (var a in Achievements.All)
        {
            var bar = Bar();
            var amount = Text("", "dim");
            var cell = MakeCell(a.Reward, () => Achievements.TryClaim(Host.State, a.Id), L.T("달성!", "Done!"));
            var card = Card(Columns(
                Icon(a.Icon),
                Column(3, Text(a.Name, "h"), Text(a.Description, "dim"), bar, amount),
                CellControl(cell)));
            _achievements.Add(new AchievementRow(a, card, bar, amount, cell));
        }
        _groups = Enumerable.Repeat(-1, _achievements.Count).ToArray();

        Content = Column(6,
            Header(L.T("오늘의 목표", "Today's Goals"), "icon_goal"),
            Text(L.T("하루 세 가지, 천천히 해도 괜찮아요. 자정이 지나면 새 목표가 와요.",
                     "Three little goals a day — no rush. New ones arrive after midnight."), "dim"),
            _goalList,
            _bonusCard,
            Header(L.T("도전과제", "Achievements"), "icon_trophy"),
            _achievementSummary,
            _achievementList);
    }

    // ── 만들기 ──

    private static Grid Columns(Control icon, Control middle, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        middle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(icon);
        grid.Children.Add(middle);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>12×12 아이콘은 2배, 큰 스프라이트(목적지 24×24)는 1배 — 칸 너비는 같게</summary>
    private static PixelIcon Icon(string sprite) => new()
    {
        Sprite = sprite,
        Scale = SpriteLibrary.TryGet(sprite, out var s) && s.Width > 16 ? 1 : 2,
        Width = 32,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static ProgressBar Bar() => new() { Minimum = 0, Maximum = 1, Height = 8 };

    private ClaimCell MakeCell(int reward, Func<bool> claim, string doneText)
    {
        var button = new Button
        {
            Classes = { "accent" },
            MinWidth = 84,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = Row(4, new PixelIcon { Sprite = "icon_candy", Scale = 1, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = L.T($"받기 +{reward}", $"Claim +{reward}"), VerticalAlignment = VerticalAlignment.Center }),
        };
        button.Click += (_, _) =>
        {
            if (!claim()) return;
            Host.Save();
            Tick();
        };
        var rewardRow = Row(4, new PixelIcon { Sprite = "icon_candy", Scale = 1, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = $"+{reward}", VerticalAlignment = VerticalAlignment.Center });
        rewardRow.HorizontalAlignment = HorizontalAlignment.Center;
        var done = new TextBlock { Text = doneText, HorizontalAlignment = HorizontalAlignment.Center };
        return new ClaimCell(button, rewardRow, done);
    }

    private static Control CellControl(ClaimCell c)
    {
        var cell = new Grid { MinWidth = 84, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(c.Claim);
        cell.Children.Add(c.Reward);
        cell.Children.Add(c.Done);
        return cell;
    }

    private void RebuildGoals(GameState s)
    {
        _goals.Clear();
        _goalList.Children.Clear();
        foreach (var g in s.Daily.Goals)
        {
            if (DailyGoals.Find(g.Id) is not { } def) continue;
            var bar = Bar();
            var amount = Text("", "dim");
            var cell = MakeCell(def.Reward, () => DailyGoals.TryClaim(Host.State, g.Id), L.T("내일 다시", "See you tomorrow"));
            var card = Card(Columns(
                Icon(def.Icon),
                Column(3, Text(def.Title(g.Target), "h"), bar, amount),
                CellControl(cell)));
            _goalList.Children.Add(card);
            _goals.Add(new GoalRow(g, def, card, bar, amount, cell));
        }
    }

    /// <summary>세이브의 목표 목록이 지금 행과 다르면 (날이 바뀜 등) true</summary>
    private bool GoalsStale(GameState s)
    {
        int known = s.Daily.Goals.Count(g => DailyGoals.Find(g.Id) != null);
        if (known != _goals.Count) return true;
        int i = 0;
        foreach (var g in s.Daily.Goals)
            if (DailyGoals.Find(g.Id) != null && !ReferenceEquals(_goals[i++].Goal, g)) return true;
        return false;
    }

    // ── 갱신 ──

    public override void Refresh() => Tick();

    public override void Tick()
    {
        var s = Host.State;
        _starlight ??= Brush("StarlightBrush");
        _good ??= Brush("GoodBrush");
        _outline ??= Brush("OutlineBrush");
        _dim ??= Brush("TextDimBrush");

        if (DailyGoals.Ensure(s, DateTimeOffset.Now) | GoalsStale(s)) RebuildGoals(s);

        foreach (var r in _goals)
        {
            double current = DailyGoals.Current(s, r.Goal);
            bool done = DailyGoals.IsDone(s, r.Goal);
            SetBar(r.Bar, DailyGoals.Progress(s, r.Goal), done);
            Set(r.Amount, $"{r.Def.Amount(Math.Min(current, r.Goal.Target))} / {r.Def.Amount(r.Goal.Target)}");
            SetCell(r.Cell, r.Card, claimable: done && !r.Goal.Claimed, finished: r.Goal.Claimed, _dim);
        }

        int claimed = s.Daily.Goals.Count(g => g.Claimed);
        Set(_bonusAmount, L.T($"받은 목표 {claimed} / {s.Daily.Goals.Count}", $"Claimed {claimed} / {s.Daily.Goals.Count}"));
        SetCell(_bonusCell, _bonusCard, DailyGoals.CanClaimBonus(s), s.Daily.BonusClaimed, _dim);

        int achieved = 0;
        bool reorder = false;
        for (int i = 0; i < _achievements.Count; i++)
        {
            var r = _achievements[i];
            bool isClaimed = Achievements.IsClaimed(s, r.Def.Id);
            bool done = isClaimed || Achievements.IsDone(s, r.Def);
            if (isClaimed) achieved++;
            double target = r.Def.Target();
            SetBar(r.Bar, isClaimed ? 1 : Achievements.Progress(s, r.Def), done);
            Set(r.Amount, isClaimed
                ? r.Def.Amount(target)
                : $"{r.Def.Amount(Math.Min(r.Def.Counter(s), target))} / {r.Def.Amount(target)}");
            SetCell(r.Cell, r.Card, claimable: done && !isClaimed, finished: isClaimed, _good);

            int group = isClaimed ? 2 : done ? 0 : 1;
            if (_groups[i] != group)
            {
                _groups[i] = group;
                reorder = true;
            }
        }
        Set(_achievementSummary, L.T($"달성 {achieved} / {_achievements.Count}", $"Completed {achieved} / {_achievements.Count}"));
        if (reorder) SortAchievements();
    }

    /// <summary>받을 수 있는 것 → 진행 중 → 받은 것 (같은 묶음 안에서는 목록 순서). 묶음이 바뀐 때만 부른다.</summary>
    private void SortAchievements()
    {
        _achievementList.Children.Clear();
        for (int group = 0; group <= 2; group++)
            for (int i = 0; i < _achievements.Count; i++)
                if (_groups[i] == group) _achievementList.Children.Add(_achievements[i].Card);
    }

    private void SetBar(ProgressBar bar, double value, bool done)
    {
        if (bar.Value != value) bar.Value = value;
        var fg = done ? _good : _starlight;
        if (!ReferenceEquals(bar.Foreground, fg)) bar.Foreground = fg;
    }

    private void SetCell(ClaimCell c, Border card, bool claimable, bool finished, IBrush? doneBrush)
    {
        c.Claim.IsVisible = claimable;
        c.Reward.IsVisible = !claimable && !finished;
        c.Done.IsVisible = finished;
        if (!ReferenceEquals(c.Done.Foreground, doneBrush)) c.Done.Foreground = doneBrush;
        var border = claimable ? _starlight : _outline;
        if (!ReferenceEquals(card.BorderBrush, border)) card.BorderBrush = border;
    }

    private static void Set(TextBlock t, string text)
    {
        if (t.Text != text) t.Text = text;
    }
}
