using Avalonia;
using CozyOrbit.App.Infra;

namespace CozyOrbit.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 두 개가 같은 세이브를 쓰면 진행·별빛이 꼬인다 → 이미 떠 있으면 그 창을 불러 내고 끝낸다
        using var instance = SingleInstance.TryAcquire();
        if (instance == null)
        {
            SingleInstance.SignalShow();
            return 0;
        }
        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, global::Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
