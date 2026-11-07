using System.Runtime.CompilerServices;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Tests.Usage;

internal static class UsageTestIsolation
{
    /// <summary>AppLog(프로바이더 실패 경고 등)가 실제 상태 폴더를 건드리지 않도록 상태 폴더를 임시 폴더로 돌린다.</summary>
    [ModuleInitializer]
    internal static void IsolateStateDirectory()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COZY_STATE_DIR"))) return;
        Environment.SetEnvironmentVariable("COZY_STATE_DIR",
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cozy-tests-state"));
    }
}

/// <summary>테스트용 임시 폴더 (Dispose 시 삭제)</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cozy-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Write(string relative, string content, DateTime? mtimeUtc = null)
    {
        var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (mtimeUtc is { } m) File.SetLastWriteTimeUtc(full, m);
        return full;
    }

    public ProviderContext Context(DateTimeOffset now, DateTimeOffset? since = null, string? customRoots = null,
        Dictionary<string, string>? env = null) => new()
    {
        Now = now,
        Since = since ?? now.AddDays(-400),
        Home = Path,
        RoamingAppData = System.IO.Path.Combine(Path, "AppData", "Roaming"),
        LocalAppData = System.IO.Path.Combine(Path, "AppData", "Local"),
        CustomRootsRaw = customRoots,
        Env = name => env != null && env.TryGetValue(name, out var v) ? v : null,
    };

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}
