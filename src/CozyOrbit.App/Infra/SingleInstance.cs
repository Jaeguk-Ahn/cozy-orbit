using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Infra;

/// <summary>
/// 단일 실행. 상태 폴더의 잠금 파일을 독점으로 열어 두고, 두 번째 실행은 "보여 줘" 신호 파일을 건드린 뒤 끝난다.
/// 첫 실행은 그 파일을 지켜보다가 창을 띄운다. 파일만 쓰므로 Windows·macOS 공통.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string LockName = "instance.lock";
    private const string SignalName = "show.signal";

    private readonly FileStream _lock;
    private FileSystemWatcher? _watcher;

    private SingleInstance(FileStream lockStream) => _lock = lockStream;

    public static SingleInstance? TryAcquire()
    {
        try
        {
            var fs = new FileStream(Path.Combine(AppPaths.StateDirectory, LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(fs);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void SignalShow()
    {
        try { File.WriteAllText(Path.Combine(AppPaths.StateDirectory, SignalName), DateTimeOffset.Now.ToString("o")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>두 번째 실행이 신호를 보내면 (임의 스레드에서) onShow 를 부른다</summary>
    public void WatchShowRequests(Action onShow)
    {
        _watcher = new FileSystemWatcher(AppPaths.StateDirectory, SignalName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += (_, _) => onShow();
        _watcher.Created += (_, _) => onShow();
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _lock.Dispose();
    }
}
