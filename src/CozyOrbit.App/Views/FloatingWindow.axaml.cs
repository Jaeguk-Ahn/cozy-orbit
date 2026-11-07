using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views;

/// <summary>
/// 업무 중에 띄워 두는 플로팅 창: 항상 위, 테두리 없음, 우주 + 연구소(햄찌)만.
/// 끌어서 옮기고, 오른쪽 아래 손잡이로 크기 조절, 두 번 누르면 큰 창. 마우스를 올리면 버튼이 나타난다.
/// 위치·크기는 설정에 기억한다 (모니터가 빠져 화면 밖이면 주 모니터 오른쪽 아래로).
/// </summary>
public partial class FloatingWindow : Window
{
    /// <summary>크기 미리 정하기 — 장면 배율이 2·3·4 가 되는 높이</summary>
    private static readonly (string Ko, string En, double W, double H)[] Presets =
    [
        ("작게", "Small", 300, 180),
        ("보통", "Medium", 400, 266),
        ("크게", "Large", 520, 352),
    ];

    private readonly GameHost _host;

    public FloatingWindow(GameHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = App.AppIcon;
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Alias);
        Scene.Host = host;
        // 플로팅에는 토스트가 없으니 발견·별사탕 알림을 선장 말풍선으로
        Scene.ShowEventBubbles = true;
        Hud.Background = new SolidColorBrush(Color.FromUInt32((Palette.SpaceDeep & 0x00FFFFFF) | 0xB0000000));

        var s = host.State.Settings;
        if (s.FloatingWidth is double w && s.FloatingHeight is double h)
        {
            Width = Math.Max(MinWidth, w);
            Height = Math.Max(MinHeight, h);
        }
        // 창이 뜨는 동안 기본 위치·크기로 한 번 PositionChanged/Resized 가 온다 — 그걸 저장하면 기억한 위치를 덮어쓴다.
        // 그래서 기억한 값으로 먼저 놓은 다음부터 기록한다.
        (int? x, int? y) saved = (s.FloatingX, s.FloatingY);
        Opened += (_, _) =>
        {
            PlaceOnScreen(saved.x, saved.y);
            PositionChanged += (_, e) =>
            {
                _host.State.Settings.FloatingX = e.Point.X;
                _host.State.Settings.FloatingY = e.Point.Y;
            };
            Resized += (_, e) =>
            {
                _host.State.Settings.FloatingWidth = e.ClientSize.Width;
                _host.State.Settings.FloatingHeight = e.ClientSize.Height;
            };
        };

        PointerEntered += (_, _) => ShowControls(true);
        PointerExited += (_, _) => ShowControls(false);
        PointerPressed += OnPointerPressed;
        DoubleTapped += (_, _) => App.Current.LeaveFloating();
        // F: 큰 창으로 (큰 창의 F 와 짝)
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.F || e.KeyModifiers != KeyModifiers.None) return;
            App.Current.LeaveFloating();
            e.Handled = true;
        };
        Grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        };
        OpenButton.Click += (_, _) => App.Current.LeaveFloating();
        CloseButton.Click += (_, _) => Close();
        ContextMenu = BuildMenu();

        host.Ticked += OnTicked;
        Closed += (_, _) =>
        {
            host.Ticked -= OnTicked;
            host.Save();
        };
        OnTicked();
    }

    private ContextMenu BuildMenu()
    {
        var open = new MenuItem { Header = L.T("큰 창으로", "Open main window") };
        open.Click += (_, _) => App.Current.LeaveFloating();
        var size = new MenuItem { Header = L.T("크기", "Size") };
        foreach (var p in Presets)
        {
            var item = new MenuItem { Header = L.T(p.Ko, p.En) };
            item.Click += (_, _) =>
            {
                Width = p.W;
                Height = p.H;
            };
            size.Items.Add(item);
        }
        var close = new MenuItem { Header = L.T("플로팅 닫기", "Close floating window") };
        close.Click += (_, _) => Close();
        var quit = new MenuItem { Header = L.T("게임 종료", "Quit game") };
        quit.Click += (_, _) => App.Current.Quit();
        return new ContextMenu { Items = { open, size, new Separator(), close, quit } };
    }

    /// <summary>
    /// 기억한 위치·크기를 가장 많이 겹치는 화면의 작업 영역 안으로 끌어다 놓는다 (크면 줄인다).
    /// 어느 화면과도 겹치지 않으면 (모니터가 빠졌으면) 주 모니터 작업 영역 오른쪽 아래.
    /// </summary>
    private void PlaceOnScreen(int? savedX, int? savedY)
    {
        Avalonia.Platform.Screen? screen = null;
        if (savedX is int x && savedY is int y)
        {
            double s0 = Screens.ScreenFromPoint(new PixelPoint(x, y))?.Scaling ?? 1;
            var saved = new PixelRect(x, y, (int)Math.Ceiling(Width * s0), (int)Math.Ceiling(Height * s0));
            screen = Screens.ScreenFromBounds(saved);
        }
        bool keep = screen != null;
        screen ??= Screens.Primary;
        if (screen == null) return;

        var area = screen.WorkingArea;
        double scaling = screen.Scaling;
        // 작업 영역보다 크면 맞춰 줄인다 (최소 크기는 지킨다)
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width / scaling));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height / scaling));
        int w = (int)Math.Ceiling(Width * scaling), h = (int)Math.Ceiling(Height * scaling);
        int maxX = Math.Max(area.X, area.Right - w), maxY = Math.Max(area.Y, area.Bottom - h);
        Position = keep
            ? new PixelPoint(Math.Clamp(savedX!.Value, area.X, maxX), Math.Clamp(savedY!.Value, area.Y, maxY))
            : new PixelPoint(Math.Max(area.X, area.Right - w - 16), Math.Max(area.Y, area.Bottom - h - 16));
    }

    private void ShowControls(bool on)
    {
        Controls.IsVisible = on;
        Grip.IsVisible = on;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1) BeginMoveDrag(e);
    }

    private void OnTicked()
    {
        var s = _host.State;
        double cap = GameRules.BatteryCapacity(s);
        string text = Fmt.Steps(s.Distance) + (GameRules.IsBoosting(s) ? L.T(" · 부스트", " · boost") : "");
        if (DistanceText.Text != text) DistanceText.Text = text;
        BatteryBar.Value = cap > 0 ? s.Starlight / cap : 0;
    }
}
