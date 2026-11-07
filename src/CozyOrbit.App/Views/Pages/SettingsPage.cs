using CozyOrbit.App.Infra;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>설정: 언어 · 창 동작 · 소리 · 사용량 소스 켜고 끄기 · 업데이트 · 기록</summary>
public sealed class SettingsPage : Page
{
    private readonly TextBlock _stats = Text("", "dim");
    /// <summary>프로바이더 id → 체크 옆의 "찾음/없음" 글</summary>
    private readonly List<(string Id, TextBlock Label)> _found = new();

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

        // 소리 (기본 꺼짐)
        root.Children.Add(Header(L.T("소리", "Sound"), "fx_note"));
        var soundOn = new CheckBox { Content = L.T("소리 켜기", "Enable sound"), IsChecked = s.Settings.SoundEnabled };
        var music = new CheckBox { Content = L.T("배경음", "Music"), IsChecked = s.Settings.MusicEnabled, MinWidth = 120 };
        var effects = new CheckBox { Content = L.T("효과음", "Sound effects"), IsChecked = s.Settings.EffectsEnabled, MinWidth = 120 };
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(s.Settings.Volume * 100), Width = 180, VerticalAlignment = VerticalAlignment.Center };
        var volumeText = Text($"{(int)volume.Value}%", "dim");
        volumeText.VerticalAlignment = VerticalAlignment.Center;
        // 생성자에서는 아직 화면에 붙지 않아 리소스를 못 찾는다 → 팔레트 색을 바로
        var soundNote = Text("", fg: Rendering.SkinArt.Solid(Core.Art.Palette.Warn));
        var volumeLabel = Text(L.T("음량", "Volume"));
        volumeLabel.VerticalAlignment = VerticalAlignment.Center;
        var detail = Column(4, Row(12, music, effects), Row(8, volumeLabel, volume, volumeText));
        void SyncSound()
        {
            detail.IsEnabled = Host.State.Settings.SoundEnabled;
            soundNote.Text = Sound.Error ?? "";
            soundNote.IsVisible = Sound.Error != null;
        }
        soundOn.IsCheckedChanged += (_, _) =>
        {
            Host.State.Settings.SoundEnabled = soundOn.IsChecked == true;
            Host.Save();
            Sound.Apply();
            // 켜자마자 들리는지 확인할 수 있게 짧은 소리 하나
            Sound.Play(Core.Audio.SoundId.Star);
            SyncSound();
        };
        music.IsCheckedChanged += (_, _) =>
        {
            Host.State.Settings.MusicEnabled = music.IsChecked == true;
            Host.Save();
            Sound.Apply();
        };
        effects.IsCheckedChanged += (_, _) =>
        {
            Host.State.Settings.EffectsEnabled = effects.IsChecked == true;
            Host.Save();
            Sound.Play(Core.Audio.SoundId.Buy);
        };
        volume.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            Host.State.Settings.Volume = Math.Round(volume.Value) / 100;
            volumeText.Text = $"{(int)Math.Round(volume.Value)}%";
            Sound.Apply();
        };
        // 놓을 때 한 번 들려 준다 (끄는 동안 계속 울리지 않게)
        volume.AddHandler(PointerReleasedEvent, (_, _) => { Host.Save(); Sound.Play(Core.Audio.SoundId.Buy); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        root.Children.Add(Text(L.T("업무 중에도 띄워 두는 게임이라 기본은 꺼져 있어요.", "Off by default, since the game stays open while you work."), "dim"));
        root.Children.Add(soundOn);
        root.Children.Add(detail);
        root.Children.Add(soundNote);
        SyncSound();

        // 사용량 소스
        root.Children.Add(Header(L.T("별빛이 되는 AI 사용량", "AI usage that becomes starlight"), "icon_starlight"));
        root.Children.Add(Text(L.T("이 컴퓨터에 남은 AI 도구 기록을 읽기만 해요. 끄면 그 도구의 사용량은 별빛이 되지 않아요.",
                                   "Only reads AI tool logs on this computer. Turn a source off to stop it from making starlight."), "dim"));
        var wrap = new WrapPanel();
        foreach (var p in host.Providers)
        {
            var box = new CheckBox { Content = p.DisplayName, IsChecked = !s.Settings.DisabledProviders.Contains(p.Id), MinWidth = 120 };
            box.IsCheckedChanged += (_, _) => Host.SetProviderEnabled(p.Id, box.IsChecked == true);
            var found = Text("", fg: Rendering.SkinArt.Solid(Core.Art.Palette.TextDim));
            found.VerticalAlignment = VerticalAlignment.Center;
            _found.Add((p.Id, found));
            var item = Row(4, box, found);
            item.MinWidth = 190;
            wrap.Children.Add(item);
        }
        root.Children.Add(wrap);

        // 업데이트
        root.Children.Add(Header(L.T("업데이트", "Updates"), "icon_voyage"));
        root.Children.Add(new UpdatePanel(host));

        // 기록
        root.Children.Add(Header(L.T("항해 기록", "Voyage log"), "icon_log"));
        root.Children.Add(_stats);
        var replay = new Button { Content = L.T("안내 다시 보기", "Show the intro again") };
        replay.Click += (_, _) => Host.ReplayOnboarding();
        root.Children.Add(replay);

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
            $"누적 거리 {Fmt.Steps(s.Stats.DistanceTotal)} · 발견 {s.Stats.Discoveries}개\n" +
            $"모은 별가루 {Fmt.Compact(s.Stats.StardustEarned)} · 받은 별빛 {Fmt.Duration(s.Stats.StarlightEarned)} · 부스트 {Fmt.Duration(s.Stats.BoostSeconds)}",
            $"Launched {s.CreatedAt.ToLocalTime():yyyy-MM-dd} · Flight time {Fmt.Duration(s.Stats.PlaySeconds)}\n" +
            $"Total distance {Fmt.Steps(s.Stats.DistanceTotal)} · {s.Stats.Discoveries} discoveries\n" +
            $"Stardust earned {Fmt.Compact(s.Stats.StardustEarned)} · Starlight received {Fmt.Duration(s.Stats.StarlightEarned)} · Boosted {Fmt.Duration(s.Stats.BoostSeconds)}");
        if (_stats.Text != text) _stats.Text = text;

        // 이 컴퓨터에서 기록 폴더를 찾았는지 (호스트가 시작할 때·스캔마다 다시 본다)
        foreach (var (id, label) in _found)
        {
            bool? found = Host.ProviderFound.TryGetValue(id, out var f) ? f : null;
            string t = found switch
            {
                true => L.T("찾음", "found"),
                false => L.T("없음", "not found"),
                null => L.T("확인 중", "checking"),
            };
            if (label.Text == t) continue;
            label.Text = t;
            label.Foreground = Rendering.SkinArt.Solid(found == true ? Core.Art.Palette.Starlight : Core.Art.Palette.TextDim);
        }
    }
}
