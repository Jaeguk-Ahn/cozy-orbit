using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using CozyOrbit.App.Views.Pages;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views;

public partial class MainWindow : Window
{
    public enum Tab { Research, Collection, Log, Shop, Settings }

    private readonly GameHost _host;
    private readonly Dictionary<Tab, (ToggleButton Button, Func<Page> Create)> _tabs;
    private readonly Dictionary<Tab, Page> _pages = new();
    private readonly Queue<(string Text, string Sprite, double Seconds)> _toasts = new();
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

        _tabs = new()
        {
            [Tab.Research] = (TabResearch, () => new ResearchPage(host)),
            [Tab.Collection] = (TabCollection, () => new CollectionPage(host)),
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

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _toastTimer.Tick += (_, _) => NextToast();

        host.Ticked += OnTicked;
        host.Discovered += OnDiscovered;
        host.Credited += OnCredited;
        host.Returned += OnReturned;
        host.Scanned += OnScanned;
        host.SkinsChanged += OnSkinsChanged;
        Closed += (_, _) =>
        {
            host.Ticked -= OnTicked;
            host.Discovered -= OnDiscovered;
            host.Credited -= OnCredited;
            host.Returned -= OnReturned;
            host.Scanned -= OnScanned;
            host.SkinsChanged -= OnSkinsChanged;
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

        SectorText.Text = L.T($"구역 {Discoveries.SectorOf(s.DiscoveryCount).Number} · {Discoveries.SectorOf(s.DiscoveryCount).Name}",
                              $"Sector {Discoveries.SectorOf(s.DiscoveryCount).Number} · {Discoveries.SectorOf(s.DiscoveryCount).Name}");
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
        double prev = k == 0 ? 0 : Balance.DiscoveryDistance(k - 1);
        double next = Balance.DiscoveryDistance(k);
        double left = Math.Max(0, next - s.Distance);
        // 도감 천체는 만나기 전까지 이름을 숨긴다
        string name = k < Discoveries.Catalog.Count ? "???" : Discoveries.NameOf(k);
        double eta = left / Math.Max(1e-9, GameRules.CurrentSpeed(s));
        NextText.Text = L.T($"다음 발견 {name} · {Fmt.Steps(left)} · 약 {Fmt.Duration(eta)}", $"Next: {name} · {Fmt.Steps(left)} · ~{Fmt.Duration(eta)}");
        NextBar.Value = Math.Clamp((s.Distance - prev) / Math.Max(1, next - prev), 0, 1);

        _current?.Tick();
    }

    private void OnDiscovered(DiscoveryEvent d)
    {
        ShowToast(L.T($"새 발견! {d.Name}\n별가루 +{Fmt.Compact(d.Reward)}", $"Discovered {d.Name}!\nStardust +{Fmt.Compact(d.Reward)}"), d.Sprite);
        _pages.GetValueOrDefault(Tab.Collection)?.Refresh();
    }

    private void OnCredited(CreditResult c, double candy)
    {
        var parts = new List<string>();
        if (c.StarlightAdded > 0) parts.Add(L.T($"별빛 +{Fmt.Duration(c.StarlightAdded)}", $"Starlight +{Fmt.Duration(c.StarlightAdded)}"));
        if (c.OverflowDust > 0) parts.Add(L.T($"넘친 별빛 → 별가루 +{Fmt.Compact(c.OverflowDust)}", $"overflow → stardust +{Fmt.Compact(c.OverflowDust)}"));
        if (candy > 0) parts.Add(L.T($"별사탕 +{Fmt.Number(candy, 1)}", $"Star candy +{Fmt.Number(candy, 1)}"));
        ShowToast(L.T("AI 사용량이 도착했어요", "Your AI usage arrived") + "\n" + string.Join(" · ", parts), candy > 0 && c.StarlightAdded <= 0 ? "icon_candy" : "icon_starlight");
        _pages.GetValueOrDefault(Tab.Shop)?.Refresh();
    }

    private void OnSkinsChanged() => _pages.GetValueOrDefault(Tab.Shop)?.Refresh();

    private void OnReturned(TickResult r)
    {
        string text = L.T($"다녀오셨어요! {Fmt.Duration(r.Seconds)} 동안\n{Fmt.Steps(r.DistanceGained)} 나아가고 별가루 +{Fmt.Compact(r.StardustGained)}",
                          $"Welcome back! In {Fmt.Duration(r.Seconds)}\nwe flew {Fmt.Steps(r.DistanceGained)}, stardust +{Fmt.Compact(r.StardustGained)}");
        if (r.Discoveries.Count > 0)
            text += L.T($"\n발견 {r.Discoveries.Count}개: {string.Join(", ", r.Discoveries.Take(3).Select(d => d.Name))}{(r.Discoveries.Count > 3 ? " …" : "")}",
                        $"\n{r.Discoveries.Count} found: {string.Join(", ", r.Discoveries.Take(3).Select(d => d.Name))}{(r.Discoveries.Count > 3 ? " …" : "")}");
        ShowToast(text, r.Discoveries.Count > 0 ? r.Discoveries[^1].Sprite : Rendering.SkinArt.Ship(_host.State).Normal, 6);
        _pages.GetValueOrDefault(Tab.Collection)?.Refresh();
    }

    private void OnScanned() => _pages.GetValueOrDefault(Tab.Log)?.Refresh();

    private void ShowToast(string text, string sprite, double seconds = 3.5)
    {
        _toasts.Enqueue((text, sprite, seconds));
        if (!Toast.IsVisible) NextToast();
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
        Toast.IsVisible = true;
        _toastTimer.Interval = TimeSpan.FromSeconds(t.Seconds);
        _toastTimer.Start();
    }
}
