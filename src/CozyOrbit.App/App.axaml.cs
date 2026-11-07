using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CozyOrbit.App.Infra;
using CozyOrbit.App.Views;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App;

public partial class App : Application
{
    /// <summary>Program 이 잡아 둔 단일 실행 잠금</summary>
    internal static SingleInstance? Instance { get; set; }

    public static new App Current => (App)Application.Current!;

    private GameHost? _host;
    private MainWindow? _main;
    private FloatingWindow? _floating;
    private TrayIcon? _tray;
    private bool _quitting;
    /// <summary>트레이 툴팁을 마지막으로 고친 때 (진행마다 고치지 않게)</summary>
    private DateTimeOffset _trayTipAt;
    private static readonly TimeSpan TrayTipInterval = TimeSpan.FromSeconds(5);

    public GameHost Host => _host!;
    /// <summary>플로팅 창이 떠 있는지 (큰 창은 숨어 있어도 플로팅 말풍선이 알림을 맡는다)</summary>
    public bool FloatingOpen => _floating != null;
    private static WindowIcon? _appIcon;
    /// <summary>Avalonia 가 뜬 뒤에만 읽을 수 있다 (정적 초기화 때는 자원 로더가 없다)</summary>
    public static WindowIcon AppIcon => _appIcon ??= new(AssetLoader.Open(new Uri("avares://CozyOrbit/Assets/app.ico")));

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AddPaletteBrushes();
    }

    /// <summary>Core 의 Palette 를 브러시 리소스로 (XAML 은 DynamicResource 로 쓴다)</summary>
    private void AddPaletteBrushes()
    {
        void Add(string key, uint argb) => Resources[key] = new SolidColorBrush(Color.FromUInt32(argb));
        Add("SpaceBrush", Palette.Space);
        Add("SpaceDeepBrush", Palette.SpaceDeep);
        Add("PanelBrush", Palette.Panel);
        Add("PanelLightBrush", Palette.PanelLight);
        Add("PanelBorderBrush", Palette.PanelBorder);
        Add("OutlineBrush", Palette.Outline);
        Add("TextBrush", Palette.Text);
        Add("TextDimBrush", Palette.TextDim);
        Add("StarlightBrush", Palette.Starlight);
        Add("StardustBrush", Palette.Stardust);
        Add("GoodBrush", Palette.Good);
        Add("WarnBrush", Palette.Warn);
        Add("CreamBrush", Palette.Cream);
        Add("GoldBrush", Palette.Gold);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = new GameHost();
            // OS 에 실제로 등록돼 있는지가 기준 (다른 곳에서 지웠을 수도 있다)
            _host.State.Settings.LaunchAtLogin = AutoStart.IsEnabled();
            bool atLogin = desktop.Args?.Contains(AutoStart.Arg) == true;
            // 지난번에 플로팅으로 두고 끝냈으면 플로팅으로 시작 (큰 창은 숨긴 채 만들어 알림만 받아 둔다).
            // 로그인 자동 실행이면 큰 창을 띄우지 않고 트레이에서 조용히 시작한다.
            if (_host.State.Settings.FloatingOpen)
            {
                EnsureMain();
                ShowFloating();
            }
            else if (atLogin)
            {
                EnsureMain();
            }
            else
            {
                ShowMain();
            }
            _host.Start();
            UpdateService.Start(_host);
            Sound.Attach(_host);
            SetupTray();
            _host.Ticked += UpdateTrayTip;
            Instance?.WatchShowRequests(() => Dispatcher.UIThread.Post(ShowMain));
            // macOS: 독 아이콘을 눌러 다시 열기
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) ShowMain(); };
            desktop.ShutdownRequested += (_, _) => Quit();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray()
    {
        var menu = new NativeMenu();
        var open = new NativeMenuItem(L.T("열기", "Open"));
        open.Click += (_, _) => ShowMain();
        var floating = new NativeMenuItem(L.T("플로팅 창", "Floating window"));
        floating.Click += (_, _) => ToggleFloating();
        var quit = new NativeMenuItem(L.T("종료", "Quit"));
        quit.Click += (_, _) => Quit();
        menu.Items.Add(open);
        menu.Items.Add(floating);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray?.Dispose();
        _tray = new TrayIcon { Icon = AppIcon, ToolTipText = "Cozy Orbit", Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        _trayTipAt = default;
        UpdateTrayTip();
    }

    /// <summary>트레이 툴팁에 지금 상태 (거리 · 다음 발견까지). 진행은 0.1초마다라 5초에 한 번만 고친다.</summary>
    private void UpdateTrayTip()
    {
        if (_tray == null || _host == null) return;
        var now = DateTimeOffset.Now;
        if (now - _trayTipAt < TrayTipInterval) return;
        _trayTipAt = now;
        var s = _host.State;
        double left = Math.Max(0, Core.Game.GameRules.NextDiscoveryDistance(s) - s.Distance);
        double eta = left / Math.Max(1e-9, Core.Game.GameRules.CurrentSpeed(s));
        string tip = $"Cozy Orbit · {Core.Game.Fmt.Steps(s.Distance)} · "
                     + L.T($"다음 발견 약 {Core.Game.Fmt.Duration(eta)}", $"next find ~{Core.Game.Fmt.Duration(eta)}");
        if (_tray.ToolTipText != tip) _tray.ToolTipText = tip;
    }

    public void ShowMain()
    {
        if (_quitting) return;
        var main = EnsureMain();
        main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    /// <summary>큰 창을 (보이지 않게라도) 만들어 둔다 — 발견·지급 알림을 받아 두려면 있어야 한다</summary>
    private MainWindow EnsureMain()
    {
        if (_main != null) return _main;
        var w = new MainWindow(Host);
        w.Closing += (_, e) =>
        {
            if (!_quitting && !w.ForceClosing && Host.State.Settings.CloseToTray)
            {
                e.Cancel = true;
                w.Hide();
            }
        };
        w.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_main, w)) return; // 언어 바꾸기로 교체된 옛 창
            _main = null;
            if (!_quitting && _floating == null) Quit();
        };
        _main = w;
        return w;
    }

    /// <summary>플로팅 창을 띄운다 (이미 떠 있으면 앞으로)</summary>
    public void ShowFloating()
    {
        if (_quitting) return;
        if (_floating == null)
        {
            var w = new FloatingWindow(Host);
            w.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_floating, w)) return;
                _floating = null;
                // 사용자가 닫은 것만 기억한다 — 종료할 때 닫힌 건 다음 실행에 다시 띄운다
                if (!_quitting) Host.State.Settings.FloatingOpen = false;
            };
            _floating = w;
        }
        Host.State.Settings.FloatingOpen = true;
        _floating.Show();
    }

    /// <summary>플로팅으로 전환: 플로팅을 띄우고 큰 창은 트레이로 숨긴다</summary>
    public void EnterFloating()
    {
        ShowFloating();
        _main?.Hide();
        Host.Save();
    }

    /// <summary>큰 창으로 돌아가기: 큰 창을 띄우고 플로팅은 닫는다</summary>
    public void LeaveFloating()
    {
        ShowMain();
        _floating?.Close();
        Host.Save();
    }

    public void ToggleFloating()
    {
        if (_floating != null) _floating.Close();
        else ShowFloating();
        Host.Save();
    }

    /// <summary>언어를 바꾸면 창·트레이 메뉴를 다시 만든다 (문자열은 만들 때 정해진다)</summary>
    public void ApplyLanguage(string code)
    {
        Host.State.Settings.Language = L.Normalize(code);
        L.SetLanguage(code);
        Host.Save();
        var oldMain = _main;
        // 창 위치·크기·최대화 상태를 새 창에 그대로 (보이기 전에 놓아야 CenterScreen 이 덮지 않는다)
        (PixelPoint Position, Size Size, WindowState State)? placement = oldMain is { } o ? (o.Position, o.ClientSize, o.WindowState) : null;
        _main = null;
        oldMain?.ForceClose();
        if (placement is { } p)
        {
            var w = EnsureMain();
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = p.Position;
            // 최대화면 지금 크기가 화면 크기라 기본 크기로 두고 상태만 옮긴다
            if (p.State == WindowState.Maximized) w.WindowState = WindowState.Maximized;
            else
            {
                w.Width = p.Size.Width;
                w.Height = p.Size.Height;
            }
        }
        ShowMain();
        _main!.SelectTab(MainWindow.Tab.Settings);
        var oldFloating = _floating;
        _floating = null;
        oldFloating?.Close();
        if (oldFloating != null) ShowFloating();
        SetupTray();
    }

    public void Quit()
    {
        if (_quitting && _host == null) return;
        _quitting = true;
        _floating?.Close();
        _main?.Close();
        Sound.Shutdown();
        _host?.Dispose();
        _host = null;
        _tray?.Dispose();
        Instance?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
