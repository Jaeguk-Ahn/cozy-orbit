using Avalonia;
using CozyOrbit.App.Infra;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 업데이트 마무리 역할로 불렸으면(옛 버전이 띄움) 화면·잠금·세이브 없이 파일만 바꾸고 끝낸다.
        // 앞으로 나올 모든 버전이 이 인자를 알아들어야 한다 — 옛 버전이 새 버전을 이렇게 부른다.
        if (UpdateInstaller.RunFinishUpdate(args) is int code) return code;

        // 두 개가 같은 세이브를 쓰면 진행·별빛이 꼬인다 → 이미 떠 있으면 그 창을 불러 내고 끝낸다.
        // 업데이트 뒤 다시 뜬 것이면 옛 프로세스가 잠금을 놓을 때까지 잠깐 기다린다 (그 창을 부르면 함께 사라진다)
        using var instance = AcquireInstance(args.Contains(UpdateInstaller.AfterUpdateArg) ? TimeSpan.FromSeconds(30) : TimeSpan.Zero);
        if (instance == null)
        {
            SingleInstance.SignalShow();
            return 0;
        }
        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, global::Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    private static SingleInstance? AcquireInstance(TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            if (SingleInstance.TryAcquire() is { } instance) return instance;
            if (DateTime.UtcNow >= deadline) return null;
            Thread.Sleep(250);
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
