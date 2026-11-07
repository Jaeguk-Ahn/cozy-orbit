namespace CozyOrbit.Core.Infra;

/// <summary>
/// 앱 상태 경로.
/// - Windows: %APPDATA%\CozyOrbit
/// - macOS: ~/Library/Application Support/CozyOrbit
/// COZY_STATE_DIR 환경 변수로 상태 폴더를 바꿀 수 있다 (개발/QA 격리용).
/// </summary>
public static class AppPaths
{
    public const string AppName = "CozyOrbit";

    public static string StateDirectory
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("COZY_STATE_DIR")?.Trim();
            var dir = !string.IsNullOrEmpty(overrideDir) ? overrideDir
                : OperatingSystem.IsMacOS() ? Path.Combine(Home, "Library", "Application Support", AppName)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>사용자 홈</summary>
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>%APPDATA% (Roaming). macOS 에서는 ~/.config 계열.</summary>
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>%LOCALAPPDATA%</summary>
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>"~" 와 %ENV% 를 펼친다.</summary>
    public static string Expand(string path)
    {
        var p = Environment.ExpandEnvironmentVariables(path.Trim());
        if (p == "~") return Home;
        if (p.StartsWith("~/") || p.StartsWith("~\\")) p = Path.Combine(Home, p[2..]);
        return p;
    }
}
