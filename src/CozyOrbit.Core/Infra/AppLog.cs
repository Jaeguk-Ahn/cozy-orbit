namespace CozyOrbit.Core.Infra;

/// <summary>간단한 파일 로그 (상태 폴더\logs\app.log, 1MB 넘으면 회전).</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    private static string LogPath
    {
        get
        {
            if (_path != null) return _path;
            var dir = Path.Combine(AppPaths.StateDirectory, "logs");
            Directory.CreateDirectory(dir);
            return _path = Path.Combine(dir, "app.log");
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = path + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로그 실패는 무시
        }
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
    }
}
