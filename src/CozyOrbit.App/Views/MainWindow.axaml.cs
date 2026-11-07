using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CozyOrbit.App.Views.Pages;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views;

public partial class MainWindow : Window
{
    public enum Tab { Research, Collection, Goals, Log, Shop, Settings }

    private readonly GameHost _host;
    private readonly Dictionary<Tab, (ToggleButton Button, Func<Page> Create)> _tabs;
    private readonly Dictionary<Tab, Page> _pages = new();
    private sealed record ToastItem(string Text, string Sprite, double Seconds);

    /// <summary>창이 숨어 있는 동안 모아 두는 일반 알림 수 (넘치면 오래된 것부터 버린다)</summary>
    private const int MaxHeldToasts = 6;

    private readonly Queue<ToastItem> _toasts = new();
    // 창이 숨어 있는 동안 (플로팅도 없을 때) 모아 두었다가 다시 보일 때 한꺼번에 알린다
    private readonly List<DiscoveryEvent> _heldFound = new();
    private TickResult? _heldReturn;
    private double _heldLight, _heldOverflowDust, _heldCandy;
    private readonly DispatcherTimer _toastTimer;
    private Page? _current;

    /// <summary>true 면 "트레이로 숨기기"를 건너뛰고 실제로 닫는다 (언어 바꾸기로 창을 교체할 때)</summary>
    public bool ForceClosing { get; private set; }

    public MainWindow(GameHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = App.AppIcon;
        // 도트 글꼴은 12px 격자에 맞춰 그렸다 → 안티에일리어싱 없이 또렷하게
        Avalonia.Media.RenderOptions.SetTextRenderingMode(this, Avalonia.Media.TextRenderingMode.Alias);
        Scene.Host = host;
        OnboardingPanel.Host = host;

        _tabs = new()
        {
            [Tab.Research] = (TabResearch, () => new ResearchPage(host)),
            [Tab.Collection] = (TabCollection, () => new CollectionPage(host)),
            [Tab.Goals] = (TabGoals, () => new GoalsPage(host)),
            [Tab.Log] = (TabLog, () => new LogPage(host)),
            [Tab.Shop] = (TabShop, () => new ShopPage(host)),
            [Tab.Settings] = (TabSettings, () => new SettingsPage(host)),
        };
        foreach (var (tab, (button, _)) in _tabs)
            button.Click += (_, _) => SelectTab(tab);

        BoostButton.Click += (_, _) => host.SetManualBoost(!host.State.ManualBoost);
        FloatButton.Click += (_, _) => App.Current.EnterFloating();
        AutoBoostBox.IsChecked = host.State.Settings.AutoBoost;
        AutoBoostBox.IsCheckedChanged += (_, _) => host.SetAutoBoost(AutoBoostBox.IsChecked == true);
        // 단축키는 툴팁에 살짝 (XAML 툴팁이 있으면 뒤에 붙인다)
        int number = 1;
        foreach (var (button, _) in _tabs.Values) AddShortcutTip(button, (number++).ToString());
        AddShortcutTip(FloatButton, "F");
        AddShortcutTip(BoostButton, "B");

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _toastTimer.Tick += (_, _) => NextToast();

        host.Ticked += OnTicked;
        host.Discovered += OnDiscovered;
        host.Credited += OnCredited;
        host.Returned += OnReturned;
        host.Scanned += OnScanned;
        host.SkinsChanged += OnSkinsChanged;
        host.VoyageReady += OnVoyageReady;
        host.VoyageStarted += OnVoyageStarted;
        host.Noticed += OnNoticed;
        Closed += (_, _) =>
        {
            host.Ticked -= OnTicked;
            host.Discovered -= OnDiscovered;
            host.Credited -= OnCredited;
            host.Returned -= OnReturned;
            host.Scanned -= OnScanned;
            host.SkinsChanged -= OnSkinsChanged;
            host.VoyageReady -= OnVoyageReady;
            host.VoyageStarted -= OnVoyageStarted;
            host.Noticed -= OnNoticed;
            _toastTimer.Stop();
        };

        SelectTab(Tab.Research);
        OnTicked();
    }

    public void ForceClose()
    {
        ForceClosing = true;
        Close();
    }

    private void OnVoyageReady() =>
        ShowToast(L.T("도감 완성! 연구 탭에서 다음 목적지로 새 항해를 떠날 수 있어요", "Atlas complete! Start a new voyage from the Research tab"), "icon_voyage", 6);

    private void OnVoyageStarted(DestinationDef dest, double candy)
    {
        ShowToast(L.T($"{dest.Name}(으)로 새 항해 시작!\n별사탕 +{Fmt.Compact(candy)}", $"New voyage to {dest.Name}!\nStar candy +{Fmt.Compact(candy)}"), dest.Sprite, 6);
        // 다른 탭은 고를 때 SelectTab 이 새로 그린다 — 화면에 없는 페이지를 건드리면 리소스를 못 찾는다
        _current?.Refresh();
    }

    public void SelectTab(Tab tab)
    {
        foreach (var (t, (button, _)) in _tabs) button.IsChecked = t == tab;
        if (!_pages.TryGetValue(tab, out var page))
        {
            page = _tabs[tab].Create();
            _pages[tab] = page;
        }
        _current = page;
        TabContent.Content = page;
        TabScroll.Offset = default;
        page.Refresh();
    }

    private void OnTicked()
    {
        var s = _host.State;
        bool boosting = GameRules.IsBoosting(s);
        double cap = GameRules.BatteryCapacity(s);

        // 받을 수 있는 목표·도전과제 수를 탭에 (0 이면 이름만)
        DailyGoals.Ensure(s, DateTimeOffset.Now);
        int claimable = Achievements.ClaimableCount(s) + s.Daily.Goals.Count(g => DailyGoals.CanClaim(s, g)) + (DailyGoals.CanClaimBonus(s) ? 1 : 0);
        string goalsLabel = L.T("목표", "Goals") + (claimable > 0 ? $" {claimable}" : "");
        if (GoalsTabText.Text != goalsLabel) GoalsTabText.Text = goalsLabel;
        // null 을 넣으면 물려받은 글자색까지 지워져 안 보인다 → 받을 게 없으면 값을 비운다
        if (claimable > 0) GoalsTabText.Foreground = (Avalonia.Media.IBrush)this.FindResource("StarlightBrush")!;
        else GoalsTabText.ClearValue(Avalonia.Controls.TextBlock.ForegroundProperty);

        var dest = Destinations.Current(s);
        var sector = Destinations.SectorOf(dest, s.DiscoveryCount);
        SectorText.Text = $"{dest.Name} · {sector.Name}";
        DistanceText.Text = Fmt.Steps(s.Distance);
        SpeedIcon.Sprite = boosting ? "icon_booster" : "icon_engine";
        SpeedText.Text = L.T($"{Fmt.Number(GameRules.CurrentSpeed(s), 2)}/초", $"{Fmt.Number(GameRules.CurrentSpeed(s), 2)}/s")
                         + (boosting ? L.T(" 부스트!", " boost!") : "");
        SpeedText.Foreground = boosting ? (Avalonia.Media.IBrush)this.FindResource("StarlightBrush")! : (Avalonia.Media.IBrush)this.FindResource("TextBrush")!;
        DustText.Text = Fmt.Compact(s.Stardust);

        BatteryBar.Value = cap > 0 ? s.Starlight / cap : 0;
        BatteryText.Text = L.T($"별빛 {Fmt.Duration(s.Starlight)} / {Fmt.Duration(cap)}", $"Starlight {Fmt.Duration(s.Starlight)} / {Fmt.Duration(cap)}");

        if (s.Settings.AutoBoost)
        {
            BoostButton.IsEnabled = false;
            BoostButton.Content = boosting ? L.T("자동 부스트 중", "Auto boosting") : L.T("별빛 기다리는 중", "Waiting for light");
        }
        else
        {
            BoostButton.IsEnabled = s.ManualBoost || s.Starlight > 0;
            BoostButton.Content = s.ManualBoost ? L.T("부스트 멈추기", "Stop boost") : L.T("부스트!", "Boost!");
        }
        if (AutoBoostBox.IsChecked != s.Settings.AutoBoost) AutoBoostBox.IsChecked = s.Settings.AutoBoost;

        int k = s.DiscoveryCount;
        double prev = GameRules.PrevDiscoveryDistance(s);
        double next = GameRules.NextDiscoveryDistance(s);
        double left = Math.Max(0, next - s.Distance);
        // 도감 천체는 만나기 전까지 이름을 숨긴다
        string name = Destinations.At(dest, k) != null ? "???" : Destinations.NameOf(dest, k);
        double eta = left / Math.Max(1e-9, GameRules.CurrentSpeed(s));
        NextText.Text = L.T($"다음 발견 {name} · {Fmt.Steps(left)} · 약 {Fmt.Duration(eta)}", $"Next: {name} · {Fmt.Steps(left)} · ~{Fmt.Duration(eta)}");
        NextBar.Value = Math.Clamp((s.Distance - prev) / Math.Max(1, next - prev), 0, 1);

        _current?.Tick();
    }

    private void OnDiscovered(DiscoveryEvent d)
    {
        switch (Mode)
        {
            case ToastMode.Show:
                Enqueue(FoundToast(new[] { d }));
                foreach (var t in SouvenirToasts(new[] { d })) Enqueue(t);
                break;
            case ToastMode.Hold:
                _heldFound.Add(d);
                break;
        }
        RefreshIfShown(Tab.Collection, Tab.Shop);
    }

    private void OnCredited(CreditResult c, double candy)
    {
        switch (Mode)
        {
            case ToastMode.Show:
                Enqueue(CreditToast(c.StarlightAdded, c.OverflowDust, candy));
                break;
            case ToastMode.Hold:
                _heldLight += c.StarlightAdded;
                _heldOverflowDust += c.OverflowDust;
                _heldCandy += candy;
                break;
        }
        RefreshIfShown(Tab.Shop, Tab.Log);
    }

    private void OnSkinsChanged() => RefreshIfShown(Tab.Shop);

    private void OnReturned(TickResult r)
    {
        switch (Mode)
        {
            case ToastMode.Show:
                Enqueue(ReturnToast(r));
                foreach (var t in SouvenirToasts(r.Discoveries)) Enqueue(t);
                break;
            case ToastMode.Hold:
                // 숨어 있는 동안 여러 번 잠들었다 깨면 한 번의 요약으로 합친다
                (_heldReturn ??= new TickResult()).Add(r);
                break;
        }
        RefreshIfShown(Tab.Collection, Tab.Shop);
    }

    private void OnScanned() => RefreshIfShown(Tab.Log);

    private void OnNoticed(string text, string sprite) => ShowToast(text, sprite, 4.5);

    /// <summary>지금 보이는 탭이 tabs 중 하나일 때만 새로 그린다 (나머지는 SelectTab 이 보여 줄 때 새로 그린다)</summary>
    private void RefreshIfShown(params Tab[] tabs)
    {
        if (_current == null) return;
        foreach (var tab in tabs)
            if (ReferenceEquals(_pages.GetValueOrDefault(tab), _current))
            {
                _current.Refresh();
                return;
            }
    }

    // ── 토스트 글 ──

    private static ToastItem FoundToast(IReadOnlyList<DiscoveryEvent> found)
    {
        if (found.Count == 1)
        {
            var d = found[0];
            return new(L.T($"새 발견! {d.Name}\n별가루 +{Fmt.Compact(d.Reward)}", $"Discovered {d.Name}!\nStardust +{Fmt.Compact(d.Reward)}"), d.Sprite, 3.5);
        }
        string names = NameList(found);
        double reward = found.Sum(d => d.Reward);
        return new(L.T($"새 발견 {found.Count}개! 별가루 +{Fmt.Compact(reward)}\n{names}", $"{found.Count} discoveries! Stardust +{Fmt.Compact(reward)}\n{names}"), found[^1].Sprite, 5);
    }

    /// <summary>발견으로 얻은 연구소 기념품 알림 (소유는 도감 기록으로 정해지므로 알리기만). 여럿이면 하나로 묶는다.</summary>
    private static IEnumerable<ToastItem> SouvenirToasts(IEnumerable<DiscoveryEvent> found)
    {
        var souvenirs = found.Where(d => d.Id != null).Select(d => Decor.SouvenirFor(d.Id!)).OfType<DecorDef>().ToList();
        if (souvenirs.Count == 0) yield break;
        if (souvenirs.Count == 1)
        {
            var s = souvenirs[0];
            yield return new(L.T($"기념품을 얻었어요: {s.Name}\n상점 → 연구소 꾸미기에서 놓을 수 있어요", $"Souvenir unlocked: {s.Name}\nPlace it from Shop → Lab Decor"), s.Sprite, 5);
            yield break;
        }
        string names = string.Join(", ", souvenirs.Take(3).Select(s => s.Name)) + (souvenirs.Count > 3 ? " …" : "");
        yield return new(L.T($"기념품 {souvenirs.Count}개를 얻었어요: {names}\n상점 → 연구소 꾸미기에서 놓을 수 있어요", $"{souvenirs.Count} souvenirs unlocked: {names}\nPlace them from Shop → Lab Decor"), souvenirs[^1].Sprite, 6);
    }

    private static ToastItem CreditToast(double light, double overflowDust, double candy)
    {
        var parts = new List<string>();
        if (light > 0) parts.Add(L.T($"별빛 +{Fmt.Duration(light)}", $"Starlight +{Fmt.Duration(light)}"));
        if (overflowDust > 0) parts.Add(L.T($"넘친 별빛 → 별가루 +{Fmt.Compact(overflowDust)}", $"overflow → stardust +{Fmt.Compact(overflowDust)}"));
        if (candy > 0) parts.Add(L.T($"별사탕 +{Fmt.Number(candy, 1)}", $"Star candy +{Fmt.Number(candy, 1)}"));
        return new(L.T("AI 사용량이 도착했어요", "Your AI usage arrived") + "\n" + string.Join(" · ", parts), candy > 0 && light <= 0 ? "icon_candy" : "icon_starlight", 3.5);
    }

    /// <summary>"다녀오셨어요" 요약. 자리 비움 상한에 걸렸으면 비운 시간과 실제로 난 시간을 함께, 침낭 연구를 귀띔한다.</summary>
    private ToastItem ReturnToast(TickResult r)
    {
        string sprite = r.Discoveries.Count > 0 ? r.Discoveries[^1].Sprite : Rendering.SkinArt.Ship(_host.State).Normal;
        string found = r.Discoveries.Count == 0 ? ""
            : L.T($"\n발견 {r.Discoveries.Count}개: {NameList(r.Discoveries)}", $"\n{r.Discoveries.Count} found: {NameList(r.Discoveries)}");
        if (r.CappedSeconds < 60)
            return new(L.T($"다녀오셨어요! {Fmt.Duration(r.Seconds)} 동안\n{Fmt.Steps(r.DistanceGained)} 나아가고 별가루 +{Fmt.Compact(r.StardustGained)}",
                           $"Welcome back! In {Fmt.Duration(r.Seconds)}\nwe flew {Fmt.Steps(r.DistanceGained)}, stardust +{Fmt.Compact(r.StardustGained)}") + found, sprite, 6);

        string away = Fmt.Duration(r.Seconds + r.CappedSeconds), flew = Fmt.Duration(r.Seconds);
        // 토스트가 탐사선 옆 좁은 자리에 뜨므로 한 줄을 짧게 끊는다 (한글은 아무 데서나 줄이 바뀐다)
        string text = L.T($"다녀오셨어요! {away} 만이에요\n그중 {flew}만 날았어요\n{Fmt.Steps(r.DistanceGained)} · 별가루 +{Fmt.Compact(r.StardustGained)}",
                          $"Welcome back! Away {away},\nbut we flew only {flew}\n{Fmt.Steps(r.DistanceGained)} · stardust +{Fmt.Compact(r.StardustGained)}");
        // 침낭을 더 키울 수 있으면 귀띔, 다 키웠으면 발견 목록
        if (!Upgrades.Get(Upgrades.Bunk).IsMaxed(_host.State.Level(Upgrades.Bunk)))
            text += L.T("\n포근한 침낭으로 더 길게!", "\nCozy Sleeping Bag → fly longer");
        else
            text += found;
        return new(text, sprite, 8);
    }

    private static string NameList(IReadOnlyList<DiscoveryEvent> found) =>
        string.Join(", ", found.Take(3).Select(d => d.Name)) + (found.Count > 3 ? " …" : "");

    // ── 토스트 띄우기·모아 두기 ──

    private enum ToastMode { Show, Hold, Drop }

    /// <summary>창이 실제로 화면에 있는지 (트레이로 숨김·최소화가 아니면)</summary>
    private bool Shown => IsVisible && WindowState != WindowState.Minimized;

    /// <summary>보이면 바로, 숨어 있으면 모아 두고, 숨어 있어도 플로팅이 떠 있으면 버린다 (플로팅 말풍선이 이미 알렸다)</summary>
    private ToastMode Mode => Shown ? ToastMode.Show : Application.Current is App { FloatingOpen: true } ? ToastMode.Drop : ToastMode.Hold;

    private void ShowToast(string text, string sprite, double seconds = 3.5)
    {
        switch (Mode)
        {
            case ToastMode.Show:
                Enqueue(new ToastItem(text, sprite, seconds));
                break;
            case ToastMode.Hold:
                if (_toasts.Any(t => t.Text == text)) return; // 같은 알림은 한 번만
                while (_toasts.Count >= MaxHeldToasts) _toasts.Dequeue();
                _toasts.Enqueue(new ToastItem(text, sprite, seconds));
                break;
        }
    }

    private void Enqueue(ToastItem toast)
    {
        _toasts.Enqueue(toast);
        if (!Toast.IsVisible) NextToast();
    }

    /// <summary>숨어 있는 동안 모은 발견·지급·다녀옴을 요약 토스트로 바꿔 줄 맨 앞에 넣는다</summary>
    private void FlushHeld()
    {
        var held = new List<ToastItem>();
        if (_heldReturn is { } r)
        {
            held.Add(ReturnToast(r));
            held.AddRange(SouvenirToasts(r.Discoveries));
        }
        if (_heldFound.Count > 0)
        {
            held.Add(FoundToast(_heldFound));
            held.AddRange(SouvenirToasts(_heldFound));
        }
        if (_heldLight > 0 || _heldOverflowDust > 0 || _heldCandy > 0)
            held.Add(CreditToast(_heldLight, _heldOverflowDust, _heldCandy));
        _heldReturn = null;
        _heldFound.Clear();
        _heldLight = _heldOverflowDust = _heldCandy = 0;
        if (held.Count == 0) return;
        var rest = _toasts.ToArray();
        _toasts.Clear();
        foreach (var t in held.Concat(rest)) _toasts.Enqueue(t);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // InitializeComponent 중에도 불린다 (타이머가 아직 없을 때)
        if (_toastTimer == null) return;
        if (change.Property != IsVisibleProperty && change.Property != WindowStateProperty) return;
        if (!Shown)
        {
            // 보이지 않는 동안엔 띄운 토스트도 시간이 가지 않게
            _toastTimer.Stop();
            return;
        }
        FlushHeld();
        if (Toast.IsVisible) _toastTimer.Start();
        else NextToast();
    }

    private void NextToast()
    {
        _toastTimer.Stop();
        if (!_toasts.TryDequeue(out var t))
        {
            Toast.IsVisible = false;
            return;
        }
        ToastText.Text = t.Text;
        ToastIcon.Sprite = t.Sprite;
        PlaceToast();
        Toast.IsVisible = true;
        _toastTimer.Interval = TimeSpan.FromSeconds(t.Seconds);
        if (Shown) _toastTimer.Start();
    }

    /// <summary>
    /// 토스트가 탐사선을 가리지 않게 탐사선 오른쪽에 둔다. 아이콘이 넓어 글 자리가 모자라면 아이콘을 1배로 줄이고,
    /// 그래도 좁거나 아직 탐사선을 안 그렸으면 가운데.
    /// </summary>
    private void PlaceToast()
    {
        const double MinText = 170, Gap = 8;
        var ship = Scene.ShipBounds;
        double left = Math.Max(12, ship.Right + Gap);
        double room = Scene.Bounds.Width - left - Gap;
        for (int scale = 2; scale >= 1; scale--)
        {
            double text = room - ToastOverhead(scale);
            if (ship.Width <= 0 || text < MinText) continue;
            Toast.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Toast.Margin = new Thickness(left, 44, 8, 0);
            ToastText.MaxWidth = Math.Min(320, text);
            return;
        }
        ToastIcon.Scale = 2;
        Toast.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        Toast.Margin = new Thickness(12, 44, 12, 0);
        ToastText.MaxWidth = 320;
    }

    /// <summary>토스트에서 글을 뺀 폭: 아이콘(스프라이트마다 다르다)·간격·안쪽 여백·테두리 + 여유 4</summary>
    private double ToastOverhead(int iconScale)
    {
        ToastIcon.Scale = iconScale;
        ToastIcon.Measure(Size.Infinity);
        return ToastIcon.DesiredSize.Width + 8 + Toast.Padding.Left + Toast.Padding.Right
               + Toast.BorderThickness.Left + Toast.BorderThickness.Right + 4;
    }

    // ── 단축키 ──

    private static void AddShortcutTip(Control control, string key) =>
        ToolTip.SetTip(control, ToolTip.GetTip(control) is string tip && tip.Length > 0 ? $"{tip} ({key})" : L.T($"단축키 {key}", $"Shortcut {key}"));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || IsTyping()) return;
        switch (e.Key)
        {
            case >= Key.D1 and <= Key.D6:
                SelectTab((Tab)(e.Key - Key.D1));
                break;
            case >= Key.NumPad1 and <= Key.NumPad6:
                SelectTab((Tab)(e.Key - Key.NumPad1));
                break;
            case Key.F:
                if (App.Current.FloatingOpen) App.Current.ToggleFloating();
                else App.Current.EnterFloating();
                break;
            case Key.B:
                // 버튼과 같은 조건 (자동 부스트 중이거나 별빛이 없으면 꺼져 있다)
                if (BoostButton.IsEnabled) _host.SetManualBoost(!_host.State.ManualBoost);
                break;
            case Key.R:
                if (!_host.Scanning) ShowToast(L.T("AI 사용량을 다시 살펴볼게요", "Checking your AI usage again"), "icon_starlight", 2);
                _ = _host.ScanNowAsync();
                break;
            case Key.Escape:
                if (!_host.State.Settings.CloseToTray) return;
                Hide();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>글을 입력하거나 목록을 고르는 중이면 단축키를 쓰지 않는다</summary>
    private bool IsTyping()
    {
        var focused = FocusManager?.GetFocusedElement() as Visual;
        return focused is TextBox or ComboBox
               || focused?.FindAncestorOfType<TextBox>() != null
               || focused?.FindAncestorOfType<ComboBox>() != null;
    }
}
