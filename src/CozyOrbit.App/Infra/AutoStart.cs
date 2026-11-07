using System.Security;
using Microsoft.Win32;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Infra;

/// <summary>
/// 로그인 시 자동 시작.
///  - Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run 의 값 CozyOrbit
///  - macOS: ~/Library/LaunchAgents/com.jaegukahn.cozyorbit.plist (RunAtLoad)
/// 실행 명령에는 항상 <see cref="Arg"/> 를 붙여, 앱이 자동 시작으로 켜졌는지 알 수 있게 한다.
/// 경로는 현재 프로세스(Environment.ProcessPath)를 쓴다. 개발 중 `dotnet run` 이면 보통 apphost(CozyOrbit.App.exe)가
/// 프로세스라 그 bin 경로가 등록되고, `dotnet CozyOrbit.App.dll` 로 띄웠다면 dotnet.exe 만 등록돼 로그인 때 앱이 아닌
/// dotnet 이 인자 없이 떠 버린다 — 자동 시작은 배포본에서 켜야 한다.
/// </summary>
internal static class AutoStart
{
    /// <summary>자동 시작으로 켜졌음을 알리는 명령줄 인자</summary>
    public const string Arg = "--autostart";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CozyOrbit";
    private const string AgentLabel = "com.jaegukahn.cozyorbit";

    private static string AgentPath => Path.Combine(AppPaths.Home, "Library", "LaunchAgents", AgentLabel + ".plist");

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) is string;
            }
            if (OperatingSystem.IsMacOS()) return File.Exists(AgentPath);
        }
        catch (Exception ex) { AppLog.Warn($"자동 시작 상태 확인 실패: {ex.Message}"); }
        return false;
    }

    /// <summary>자동 시작을 켜거나 끈다. 실패하면(지원하지 않는 OS 포함) 로그를 남기고 false.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return SetWindows(enabled);
            if (OperatingSystem.IsMacOS()) return SetMac(enabled);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"자동 시작 설정 실패: {ex.Message}");
        }
        return false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool SetWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            key.SetValue(ValueName, $"\"{exe}\" {Arg}");
        }
        else if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, false);
        }
        return true;
    }

    private static bool SetMac(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(AgentPath)) File.Delete(AgentPath);
            return true;
        }
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key><string>{AgentLabel}</string>
                <key>ProgramArguments</key>
                <array><string>{SecurityElement.Escape(exe)}</string><string>{Arg}</string></array>
                <key>RunAtLoad</key><true/>
                <key>ProcessType</key><string>Interactive</string>
            </dict>
            </plist>
            """;
        File.WriteAllText(AgentPath, plist);
        return true;
    }
}
