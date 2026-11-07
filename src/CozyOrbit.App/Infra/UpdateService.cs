using Avalonia.Controls;
using Avalonia.Threading;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Infra;

/// <summary>
/// 앱 쪽 업데이트 일정·상태. 시작 10초 뒤 한 번, 그 뒤 6시간마다 (설정의 CheckUpdates 가 켜져 있을 때만) GitHub 릴리스를 묻는다.
/// 무엇이 있는지는 UpdateChecker, 설치는 UpdateInstaller — 여기는 언제 묻고 언제 끝낼지만 정한다.
/// 개발 빌드(배포판 없음)는 새 버전을 알리기만 하고 설치하지 않는다 (UpdateInstaller.Blocker.DevBuild).
/// </summary>
public static class UpdateService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    // 헤더까지의 시간 제한 — 본문(zip)은 UpdateInstaller 가 읽기마다 멈춤을 따로 본다
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static GameHost? _host;
    private static DispatcherTimer? _timer;
    private static bool _cleaned;

    public static UpdateChecker Checker { get; }
    public static UpdateInstaller Installer { get; }

    /// <summary>상태가 바뀜 (늘 화면 스레드에서)</summary>
    public static event Action? Changed;

    static UpdateService()
    {
        var assembly = typeof(UpdateService).Assembly;
        var build = UpdateChecker.BuildInfo.FromAssembly(assembly);
        var version = UpdateChecker.CurrentVersionOf(assembly);
        Checker = new UpdateChecker(version, build, Http);
        Installer = new UpdateInstaller(Http, build.Flavor, Checker.CurrentVersion);
        Checker.Changed += RaiseChanged;
        Installer.Changed += RaiseChanged;
    }

    /// <summary>앱 시작 때 한 번. 확인 여부는 매번 설정을 보고 정하므로 나중에 켜도 다음 차례부터 묻는다</summary>
    public static void Start(GameHost host)
    {
        _host = host;
        if (_timer != null) return;
        _timer = new DispatcherTimer(FirstDelay, DispatcherPriority.Background, (_, _) => OnTimer());
        _timer.Start();
    }

    private static void OnTimer()
    {
        _timer!.Interval = Interval;
        if (!_cleaned)
        {
            // 지난 업데이트가 남긴 옛 exe·받은 파일 — 마무리 프로세스가 끝날 틈을 두고 지운다
            _cleaned = true;
            Installer.CleanupLeftovers();
        }
        if (_host?.State.Settings.CheckUpdates == true) _ = CheckNowAsync();
    }

    /// <summary>[지금 확인] — 설정과 관계없이 바로 묻는다</summary>
    public static Task CheckNowAsync() => Checker.CheckAsync();

    /// <summary>자동 확인 켜고 끄기 (저장까지). 켰는데 아직 묻지 않았으면 바로 묻는다</summary>
    public static void SetAutoCheck(bool on)
    {
        if (_host == null) return;
        _host.State.Settings.CheckUpdates = on;
        _host.Save();
        if (on && Checker.LastStatus == UpdateChecker.Status.NotChecked) _ = CheckNowAsync();
    }

    /// <summary>지금 새 버전을 여기서 설치할 수 있나</summary>
    public static bool CanInstall => Installer.CanInstall(Checker.Available);

    /// <summary>[설치하고 다시 시작] — 새 버전이 넘겨받았으면 저장하고 앱을 끝낸다. 실패하면 지금 버전 그대로 (Installer.Error)</summary>
    public static async Task InstallAsync()
    {
        if (Checker.Available is not { } release || !Installer.CanInstall(release)) return;
        _host?.Save();
        if (await Installer.InstallAsync(release)) App.Current.Quit();
    }

    /// <summary>릴리스 페이지 (새 버전이 있으면 그 페이지, 없으면 릴리스 목록) — 이 저장소 주소만 연다</summary>
    public static async Task OpenReleasePageAsync(TopLevel? top)
    {
        var url = Checker.Available?.PageUrl is { } page && UpdateChecker.IsRepoUrl(page, "/releases/") ? page : UpdateChecker.ReleasesPageUrl;
        if (top?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(url));
    }

    private static void RaiseChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }
}
