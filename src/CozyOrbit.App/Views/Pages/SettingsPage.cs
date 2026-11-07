using CozyOrbit.App.Infra;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>설정: 언어 · 창 동작 · 사용량 소스 켜고 끄기 · 기록</summary>
public sealed class SettingsPage : Page
{
    private readonly TextBlock _stats = Text("", "dim");

    public SettingsPage(GameHost host) : base(host)
    {
        var s = host.State;
        var root = Column(6);

        // 언어
        root.Children.Add(Header(L.T("언어", "Language"), "icon_book"));
        var lang = new ComboBox { ItemsSource = L.Supported.Select(L.DisplayName).ToList(), SelectedIndex = L.IsKorean ? 0 : 1, MinWidth = 140 };
        lang.SelectionChanged += (_, _) =>
        {
            var code = L.Supported[Math.Max(0, lang.SelectedIndex)];
            if (code != L.Language) App.Current.ApplyLanguage(code);
        };
        root.Children.Add(lang);

        // 창
        root.Children.Add(Header(L.T("창", "Window"), "icon_gear"));
        var tray = new CheckBox { Content = L.T("창을 닫으면 트레이로 숨기기 (계속 항해)", "Closing the window hides it to the tray (keeps flying)"), IsChecked = s.Settings.CloseToTray };
        tray.IsCheckedChanged += (_, _) =>
        {
            Host.State.Settings.CloseToTray = tray.IsChecked == true;
            Host.Save();
        };
        root.Children.Add(tray);
        var login = new CheckBox
        {
            Content = L.T("로그인하면 자동으로 시작 (켜 둔 동안만 별사탕이 쌓여요)", "Start at login (star candy only builds while running)"),
            IsChecked = s.Settings.LaunchAtLogin,
        };
        login.IsCheckedChanged += (_, _) =>
        {
            bool want = login.IsChecked == true;
            if (want == Host.State.Settings.LaunchAtLogin) return;
            // OS 등록에 실패하면 체크를 되돌린다 (개발 실행·권한 문제 등 — 로그에 이유)
            if (!AutoStart.SetEnabled(want))
            {
                login.IsChecked = !want;
                return;
            }
            Host.State.Settings.LaunchAtLogin = want;
            Host.Save();
        };
        root.Children.Add(login);
        var floating = new Button { Content = L.T("플로팅 창 열기/닫기 (항상 위, 우주선·햄찌만)", "Toggle floating window (always on top, scene only)") };
        floating.Click += (_, _) => App.Current.ToggleFloating();
        root.Children.Add(floating);

        // 사용량 소스
        root.Children.Add(Header(L.T("별빛이 되는 AI 사용량", "AI usage that becomes starlight"), "icon_starlight"));
        root.Children.Add(Text(L.T("이 컴퓨터에 남은 AI 도구 기록을 읽기만 해요. 끄면 그 도구의 사용량은 별빛이 되지 않아요.",
                                   "Only reads AI tool logs on this computer. Turn a source off to stop it from making starlight."), "dim"));
        var wrap = new WrapPanel();
        foreach (var p in host.Providers)
        {
            var box = new CheckBox { Content = p.DisplayName, IsChecked = !s.Settings.DisabledProviders.Contains(p.Id), MinWidth = 150 };
            box.IsCheckedChanged += (_, _) => Host.SetProviderEnabled(p.Id, box.IsChecked == true);
            wrap.Children.Add(box);
        }
        root.Children.Add(wrap);

        // 업데이트
        root.Children.Add(Header(L.T("업데이트", "Updates"), "icon_voyage"));
        root.Children.Add(new UpdatePanel(host));

        // 기록
        root.Children.Add(Header(L.T("항해 기록", "Voyage log"), "icon_log"));
        root.Children.Add(_stats);

        var folder = new Button { Content = L.T("저장 폴더 열기", "Open save folder") };
        folder.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppPaths.StateDirectory));
        };
        var quit = new Button { Content = L.T("게임 종료", "Quit game") };
        quit.Click += (_, _) => App.Current.Quit();
        var buttons = Row(6, folder, quit);
        buttons.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        root.Children.Add(buttons);

        Content = root;
    }

    public override void Refresh() => Tick();

    public override void Tick()
    {
        var s = Host.State;
        var text = L.T(
            $"출발한 날 {s.CreatedAt.ToLocalTime():yyyy-MM-dd} · 항해 시간 {Fmt.Duration(s.Stats.PlaySeconds)}\n" +
            $"누적 거리 {Fmt.Steps(s.Distance)} · 발견 {s.DiscoveryCount}개\n" +
            $"모은 별가루 {Fmt.Compact(s.Stats.StardustEarned)} · 받은 별빛 {Fmt.Duration(s.Stats.StarlightEarned)} · 부스트 {Fmt.Duration(s.Stats.BoostSeconds)}",
            $"Launched {s.CreatedAt.ToLocalTime():yyyy-MM-dd} · Flight time {Fmt.Duration(s.Stats.PlaySeconds)}\n" +
            $"Distance {Fmt.Steps(s.Distance)} · {s.DiscoveryCount} discoveries\n" +
            $"Stardust earned {Fmt.Compact(s.Stats.StardustEarned)} · Starlight received {Fmt.Duration(s.Stats.StarlightEarned)} · Boosted {Fmt.Duration(s.Stats.BoostSeconds)}");
        if (_stats.Text != text) _stats.Text = text;
    }
}
