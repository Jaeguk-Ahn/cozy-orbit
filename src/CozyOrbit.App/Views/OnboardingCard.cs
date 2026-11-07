using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views;

/// <summary>
/// 첫 실행 안내: 선장 햄찌가 세 마디로 알려 준다 (혼자 나는 배 · 토큰 → 별빛 · 별가루·도감·상점).
/// 다 보거나 닫으면 GameSettings.OnboardingDone 에 남고, 설정의 "안내 다시 보기"로 처음부터 다시 뜬다.
/// </summary>
public sealed class OnboardingCard : Border
{
    private const int StepCount = 3;

    private readonly PixelIcon _captain = new() { Scale = 3, Animate = true, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _step = new() { Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { Classes = { "h" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _body = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
    private readonly Button _close = new() { Content = L.T("닫기", "Close") };
    private readonly Button _next = new() { Classes = { "accent" }, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };

    private GameHost? _host;
    private int _index;

    public OnboardingCard()
    {
        // 생성자에서는 리소스를 못 찾는다 → 팔레트 색을 바로
        Classes.Add("card");
        BorderBrush = SkinArt.Solid(Palette.Starlight);
        Padding = new Thickness(10, 8);
        IsVisible = false;

        _close.Click += (_, _) => Finish();
        _next.Click += (_, _) =>
        {
            if (_index + 1 >= StepCount) Finish();
            else Show(_index + 1);
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        header.Children.Add(_title);
        Grid.SetColumn(_step, 1);
        header.Children.Add(_step);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _close, _next },
        };
        var text = new StackPanel { Spacing = 4, Children = { header, _body, buttons } };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        layout.Children.Add(_captain);
        Grid.SetColumn(text, 1);
        layout.Children.Add(text);
        Child = layout;
    }

    /// <summary>창이 만들어질 때 한 번 (아직 안내를 안 봤으면 첫 마디부터)</summary>
    public GameHost? Host
    {
        get => _host;
        set
        {
            if (_host != null && _attached) _host.OnboardingRequested -= Restart;
            _host = value;
            if (_host != null && _attached) _host.OnboardingRequested += Restart;
            if (_host != null && !_host.State.Settings.OnboardingDone) Show(0);
            else IsVisible = false;
        }
    }

    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_host != null) _host.OnboardingRequested += Restart;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_host != null) _host.OnboardingRequested -= Restart;
        base.OnDetachedFromVisualTree(e);
    }

    private void Restart() => Show(0);

    private void Show(int index)
    {
        if (_host == null) return;
        _index = index;
        var s = _host.State;
        _captain.Sprite = SkinArt.CaptainFur(s);
        _step.Text = $"{index + 1}/{StepCount}";
        (_title.Text, _body.Text) = index switch
        {
            0 => (L.T("도토리호는 혼자서도 날아요", "The acorn ship flies by itself"),
                  L.T($"창을 닫아 두어도 항해는 계속돼요.\n자리를 비운 동안엔 최대 {Fmt.Duration(GameRules.OfflineCap(s).TotalSeconds)}까지 다녀와요.",
                      $"It keeps sailing even while the window is closed — up to {Fmt.Duration(GameRules.OfflineCap(s).TotalSeconds)} while you're away.")),
            1 => (L.T("AI 토큰이 별빛이 돼요", "AI tokens become starlight"),
                  L.T("이 컴퓨터에서 AI 도구를 쓰면 그 토큰이 별빛으로 모여 부스트가 돼요. 이 컴퓨터의 기록을 읽기만 해요.",
                      "Tokens you use with AI tools on this PC gather as starlight and power the boost. It only reads logs on this computer.")),
            _ => (L.T("별가루로 연구하고 도감을 채워요", "Research, discover, collect"),
                  L.T("별가루로 연구소를 키우고, 새 천체를 만나면 도감이 채워져요. 별사탕은 상점에서 옷과 소품으로 바꿔요.",
                      "Spend stardust on research, and each new discovery fills the atlas. Star candy buys outfits and decor in the shop.")),
        };
        _next.Content = index + 1 >= StepCount ? L.T("출발!", "Let's go!") : L.T("다음", "Next");
        IsVisible = true;
    }

    private void Finish()
    {
        IsVisible = false;
        _host?.FinishOnboarding();
    }
}
