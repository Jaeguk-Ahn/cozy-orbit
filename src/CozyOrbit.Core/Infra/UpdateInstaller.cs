using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CozyOrbit.Core.Infra;

/// <summary>
/// 새 버전 내려받아 설치하고 다시 시작 (UpdateChecker 가 찾은 릴리스). poke-token-bar-window 의 설치 방식을 옮겨 왔다.
/// 순서: GitHub 릴리스 첨부(이 저장소의 releases/download 만) 받기 → 서명된 매니페스트의 크기·sha256 확인 → 풀기 → (아래) → 새 버전 실행.
///
/// **넘기기(기본)**: 옛 앱은 자기 파일을 건드리지 않는다. 풀어 둔 새 버전을 --finish-update 로 띄우고, 그쪽이 "준비됨" 파일을
/// 만들면 평소처럼 끝난다. 새 버전은 옛 프로세스가 끝나길 기다렸다가 설치 자리의 파일을 바꾸고 그 자리에서 --after-update 로 다시 띄운다.
/// 단일 파일 exe 는 실행 중에도 아직 안 쓴 코드를 자기 exe 경로에서 읽는데, 실행 중에 그 경로를 다른 버전으로 바꾸면 그 읽기가 실패한다
/// — 그래서 옛 프로세스가 도는 동안에는 바꾸지 않는다.
///  - Windows(단일 exe): exe 옆의 .cozyorbit-update 폴더에 풀고, 설치 자리의 exe → exe.old, 새 exe 를 그 자리로 복사. 경로가 같아 자동 시작은 그대로.
///  - macOS(.app): 번들을 .app.old 로 옮기고 새 번들을 ditto 로 같은 자리에. App Translocation(옮기지 않고 실행)이면 못 한다.
///
/// **직접 바꾸기(대체)**: 새 버전이 뜨지 못하면(실행 정책 등) 실행 중에 바꾼다 — 실패하면 옛 것을 되돌린다. 그 뒤 다시 띄우기가 실패해도
/// 파일은 이미 새것이라 "다시 시작하면 열려요" 로 안내하고 같은 버전을 또 바꾸지 않는다.
/// .old·받은 파일은 다음 시작 때 지운다. 개발 빌드(배포판 없음)·설치 폴더에 쓸 수 없음·이 배포판 파일 없음이면 설치하지 않는다 — 앱은 릴리스 페이지를 연다.
/// </summary>
public sealed class UpdateInstaller
{
    public enum Kind { Unsupported, WindowsExe, MacBundle }

    /// <summary>새 프로세스에 붙이는 인자 — 시작 코드가 보고 단일 실행 잠금을 기다린다</summary>
    public const string AfterUpdateArg = "--after-update";

    /// <summary>
    /// 넘기기: 풀어 둔 새 버전을 이 인자로 띄운다 — 화면 없이 파일만 바꾸고 끝난다 (앱의 Main 이 <see cref="RunFinishUpdate"/> 로 먼저 본다).
    /// 인자 순서: --finish-update &lt;exe|app&gt; &lt;설치 자리&gt; &lt;새 버전 위치&gt; &lt;옛 프로세스 id&gt; &lt;준비됨 파일&gt;
    /// 앞으로 나올 모든 버전이 이 인자를 알아들어야 한다 (옛 버전이 새 버전을 이렇게 부른다).
    /// </summary>
    public const string FinishUpdateArg = "--finish-update";

    /// <summary>넘기기 요청 (테스트에서 바꿔 끼운다)</summary>
    public sealed record HandOff(string Helper, IReadOnlyList<string> Args, string ReadyFile);

    private readonly HttpClient _http;
    private readonly string? _flavor;
    private readonly string _currentVersion;
    private readonly string? _processPath;
    private readonly string _workDir;
    private readonly Func<string, bool> _relaunch;
    private readonly Func<HandOff, bool> _handOff;
    private readonly HashSet<string> _failedVersions = new();
    /// <summary>직접 바꾸기로 파일은 이미 바꾼 버전 — 다시 받아 또 바꾸지 않는다</summary>
    private readonly HashSet<string> _swappedVersions = new();
    private int _busy;

    /// <summary>스스로 설치하지 못하는 까닭 (언어를 바꿔도 맞는 문장이 나오게 글이 아닌 값으로 둔다)</summary>
    public enum Blocker { None, DevBuild, Environment, WrongOs, Translocated, ReadOnly }

    public Kind InstallKind { get; }
    public Blocker Blocked { get; }
    /// <summary>스스로 설치할 수 없는 이유 (설정에 보여 준다) — 할 수 있으면 null</summary>
    public string? UnsupportedReason => Blocked switch
    {
        Blocker.None => null,
        Blocker.DevBuild => L.T("개발 빌드라 자동 설치는 하지 않아요 — 릴리스 페이지에서 받아 주세요.", "This is a development build, so it won't install automatically — use the release page."),
        Blocker.WrongOs => L.T("이 배포판은 이 운영체제용이 아니에요 — 릴리스 페이지에서 받아 주세요.", "This build isn't for this operating system — use the release page."),
        Blocker.Translocated => L.T("앱을 응용 프로그램 폴더로 옮긴 뒤 다시 실행하면 자동 설치할 수 있어요.", "Move the app to the Applications folder and reopen it to install updates automatically."),
        Blocker.ReadOnly => L.T("설치 폴더에 쓸 수 없어요 — 릴리스 페이지에서 받아 바꿔 주세요.", "Can't write to the install folder — download it from the release page instead."),
        _ => L.T("이 환경에서는 자동 설치를 할 수 없어요.", "Automatic install isn't available here."),
    };
    /// <summary>내려받는 중 0~1, 아니면 null</summary>
    public double? Progress { get; private set; }
    public bool IsBusy => Volatile.Read(ref _busy) == 1;
    /// <summary>마지막 설치 실패 (사용자에게 보여 줄 문장)</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    /// <param name="flavor">이 빌드의 배포판 (UpdateChecker.BuildInfo) — null 이면 개발 빌드라 설치하지 않는다</param>
    /// <param name="currentVersion">지금 버전 (요청의 User-Agent 용)</param>
    /// <param name="processPath">실행 중인 파일 (기본 Environment.ProcessPath)</param>
    /// <param name="kind">설치 방식 (기본: OS 로 판단)</param>
    /// <param name="workDir">받은 파일·푼 파일을 둘 곳 (기본: Windows 는 exe 옆 .cozyorbit-update, macOS 는 ~/Library/Caches/CozyOrbit/updates)</param>
    /// <param name="relaunch">새 버전 실행 (기본: 실제 프로세스 시작) — 시작했으면 true. 직접 바꾸기에서만 쓴다</param>
    /// <param name="handOff">새 버전에 넘기기 (기본: 띄우고 "준비됨" 파일을 기다림) — 넘겼으면 true, 못 넘기면 직접 바꾸기로</param>
    public UpdateInstaller(HttpClient http, string? flavor, string currentVersion, string? processPath = null, Kind? kind = null,
        string? workDir = null, Func<string, bool>? relaunch = null, Func<HandOff, bool>? handOff = null)
    {
        _http = http;
        _flavor = flavor;
        _currentVersion = currentVersion;
        _processPath = processPath ?? Environment.ProcessPath;
        InstallKind = kind ?? DetectKind(_processPath);
        _workDir = workDir ?? DefaultWorkDir(InstallKind, _processPath);
        _relaunch = relaunch ?? (path => Relaunch(InstallKind, path));
        _handOff = handOff ?? StartHelper;
        Blocked = FindBlocker();
    }

    private static string DefaultWorkDir(Kind kind, string? processPath) =>
        kind == Kind.WindowsExe && Path.GetDirectoryName(processPath) is { Length: > 0 } dir
            ? Path.Combine(dir, ".cozyorbit-update")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(AppPaths.Home, "Library", "Caches", AppPaths.AppName, "updates")
                : Path.Combine(AppPaths.LocalAppData, AppPaths.AppName, "updates");

    /// <summary>이 릴리스를 여기서 바로 설치할 수 있나</summary>
    public bool CanInstall(UpdateChecker.Release? release) =>
        release?.Asset != null && release.DownloadUrl != null && UnsupportedReason == null && !IsBusy && !_swappedVersions.Contains(release.Version);

    /// <summary>이번 실행에서 이미 실패한 버전 (자동 설치가 새로고침마다 다시 시도하지 않게)</summary>
    public bool FailedBefore(string version) => _failedVersions.Contains(version) || _swappedVersions.Contains(version);

    /// <summary>
    /// 받아서 바꾸고 새 버전을 띄운다. true 면 새 버전이 시작됐으니 부른 쪽이 앱을 끝낸다.
    /// 실패하면 지금 버전은 그대로 두고 Error 에 이유를 남긴다.
    /// </summary>
    public async Task<bool> InstallAsync(UpdateChecker.Release release, CancellationToken ct = default)
    {
        if (!CanInstall(release) || Interlocked.Exchange(ref _busy, 1) == 1) return false;
        Error = null;
        Progress = 0;
        Changed?.Invoke();
        try
        {
            var staged = await PrepareAsync(release, ct);
            var handOff = MakeHandOff(staged);
            if (await Task.Run(() => _handOff(handOff), ct)) // 준비 신호를 최대 15초 기다린다 — 화면 스레드를 막지 않게
            {
                AppLog.Info($"업데이트 설치: v{release.Version} ({release.Asset!.Name}) — 새 버전이 이 앱이 끝난 뒤 마무리");
                return true;
            }

            // 대체: 직접 바꾸기. 바꾼 뒤에는 이 프로세스가 처음 쓰는 코드를 못 읽을 수 있다 → 새 버전 실행에 쓸 형식을 미리 불러 둔다
            AppLog.Warn($"업데이트: 새 버전에 넘기지 못해 직접 바꿔요 (v{release.Version})");
            PrewarmRelaunch();
            var launch = Apply(staged);
            _swappedVersions.Add(release.Version);
            AppLog.Info($"업데이트 설치: v{release.Version} ({release.Asset!.Name})");
            bool started;
            try { started = _relaunch(launch); }
            catch (Exception ex)
            {
                AppLog.Warn($"새 버전 실행 실패 ({ex.GetType().Name}): {ex.Message}");
                started = false;
            }
            if (started) return true;
            Error = L.T("설치했어요. 앱을 다시 시작하면 새 버전이 열려요.", "Installed. Restart the app to open the new version.");
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            _failedVersions.Add(release.Version);
            AppLog.Warn($"업데이트 설치 실패 (v{release.Version}, {ex.GetType().Name}): {ex.Message}");
            Error = L.T($"설치하지 못했어요: {ex.Message}", $"Couldn't install: {ex.Message}");
            return false;
        }
        finally
        {
            Progress = null;
            Volatile.Write(ref _busy, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>받기 + 확인 + 풀기 → 바꿔 넣을 것의 경로 (Windows: 새 exe, macOS: 새 .app)</summary>
    internal async Task<string> PrepareAsync(UpdateChecker.Release release, CancellationToken ct)
    {
        var asset = release.Asset ?? throw new UpdateInstallException("no asset for this build");
        var url = release.DownloadUrl is { } u && UpdateChecker.IsRepoUrl(u, "/releases/download/") ? u : throw new UpdateInstallException("untrusted download address");
        Directory.CreateDirectory(_workDir);
        var zip = Path.Combine(_workDir, asset.Name);
        await DownloadAsync(url, asset, zip, ct);

        var stage = Path.Combine(_workDir, "stage-" + release.Version);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        try
        {
            if (InstallKind == Kind.MacBundle)
            {
                // ditto 는 실행 권한·서명·확장 속성을 그대로 푼다 (.NET ZipFile 은 .app 의 서명을 깨뜨릴 수 있다)
                Run("ditto", "-x", "-k", zip, stage);
                var app = Directory.GetDirectories(stage, "*.app") is [var a] ? a : throw new UpdateInstallException("no .app in the zip");
                var inner = Path.Combine(app, "Contents", "MacOS", Path.GetFileName(_processPath ?? "CozyOrbit"));
                return File.Exists(inner) ? app : throw new UpdateInstallException("the .app has no CozyOrbit executable");
            }
            ZipFile.ExtractToDirectory(zip, stage); // ../ 같은 경로는 .NET 이 거절한다
            var exe = Directory.GetFiles(stage, "*.exe") is [var e] ? e : throw new UpdateInstallException("no single .exe in the zip");
            // 이름이 다르면 설치 자리(바로 가기·자동 시작이 가리키는 파일)와 어긋난다
            return string.Equals(Path.GetFileName(exe), Path.GetFileName(_processPath), StringComparison.OrdinalIgnoreCase)
                ? exe : throw new UpdateInstallException("unexpected executable name in the zip");
        }
        finally
        {
            try { File.Delete(zip); } catch (IOException) { }
        }
    }

    private async Task DownloadAsync(string url, UpdateAsset asset, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        UpdateChecker.AddHeaders(req, _currentVersion);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new UpdateInstallException($"download failed ({(int)resp.StatusCode})");
        if (resp.Content.Headers.ContentLength is long length && length != asset.Size) throw new UpdateInstallException("unexpected file size");

        var partial = path + ".partial";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        int lastPercent = -1;
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(partial))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    // 멈춘 연결: 한 번 읽기에 1분 넘게 아무것도 안 오면 포기 (HttpClient.Timeout 은 헤더까지만 본다)
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    stall.CancelAfter(TimeSpan.FromMinutes(1));
                    int n = await src.ReadAsync(buffer, stall.Token);
                    if (n == 0) break;
                    total += n;
                    if (total > asset.Size) throw new UpdateInstallException("file is larger than expected");
                    hash.AppendData(buffer, 0, n);
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    int percent = (int)(total * 100 / asset.Size);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        Progress = total / (double)asset.Size;
                        Changed?.Invoke();
                    }
                }
            }
            if (total != asset.Size) throw new UpdateInstallException("file is smaller than expected");
            if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != asset.Sha256) throw new UpdateInstallException("checksum mismatch");
            File.Move(partial, path, true);
        }
        catch
        {
            // 확인을 통과하지 못한 바이트는 남기지 않는다
            try { File.Delete(partial); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>실행 중인 파일을 새것으로 바꾼다 — 중간에 실패하면 원래대로 돌려놓는다. 새 버전을 띄울 경로를 돌려준다</summary>
    internal string Apply(string staged)
    {
        var target = _processPath ?? throw new UpdateInstallException("unknown executable path");
        if (InstallKind == Kind.MacBundle)
        {
            var bundle = BundleOf(target) ?? throw new UpdateInstallException("not inside an .app bundle");
            var oldBundle = bundle + ".old";
            if (Directory.Exists(oldBundle)) Directory.Delete(oldBundle, true);
            Directory.Move(bundle, oldBundle);
            try { Run("ditto", staged, bundle); }
            catch
            {
                try { if (Directory.Exists(bundle)) Directory.Delete(bundle, true); } catch (IOException) { }
                Directory.Move(oldBundle, bundle);
                throw;
            }
            return bundle;
        }

        var old = target + ".old";
        try { if (File.Exists(old)) File.Delete(old); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { old = $"{target}.{Guid.NewGuid():N}.old"; } // 아직 잠긴 옛 파일
        File.Move(target, old);
        try { File.Move(staged, target); }
        catch
        {
            File.Move(old, target);
            throw;
        }
        return target;
    }

    // ---------------- 넘기기 ----------------

    /// <summary>풀어 둔 새 버전을 마무리 역할로 띄울 요청 (설치 자리·새 버전 위치·이 프로세스 id·준비됨 파일)</summary>
    internal HandOff MakeHandOff(string staged)
    {
        var target = _processPath ?? throw new UpdateInstallException("unknown executable path");
        bool mac = InstallKind == Kind.MacBundle;
        var installed = mac ? BundleOf(target) ?? throw new UpdateInstallException("not inside an .app bundle") : target;
        // 맥: 새 번들 안의 실행 파일을 그대로 띄운다 (open 을 거치지 않음 — 화면 없이 끝나는 일이라)
        var helper = mac ? Path.Combine(staged, "Contents", "MacOS", Path.GetFileName(target)) : staged;
        var ready = Path.Combine(_workDir, $"ready-{Guid.NewGuid():N}");
        return new HandOff(helper,
            [FinishUpdateArg, mac ? "app" : "exe", installed, staged, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), ready],
            ready);
    }

    /// <summary>새 버전을 띄우고 "준비됨" 파일을 기다린다. 먼저 끝나 버리거나 15초 안에 신호가 없으면 false (→ 직접 바꾸기)</summary>
    private static bool StartHelper(HandOff h)
    {
        Process? p = null;
        try
        {
            var psi = new ProcessStartInfo(h.Helper) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(h.Helper) ?? "" };
            foreach (var a in h.Args) psi.ArgumentList.Add(a);
            p = Process.Start(psi);
            if (p == null) return false;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(h.ReadyFile)) return true;
                if (p.HasExited) break;
                Thread.Sleep(100);
            }
            if (File.Exists(h.ReadyFile)) return true;
            AppLog.Warn(p.HasExited ? $"업데이트: 새 버전이 마무리 전에 끝남 ({p.ExitCode})" : "업데이트: 새 버전이 준비 신호를 보내지 않음");
            if (!p.HasExited) p.Kill(); // 신호가 없는 채로 나중에 파일을 바꾸지 않게
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"업데이트: 새 버전을 띄우지 못함 ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
        finally
        {
            p?.Dispose();
        }
    }

    /// <summary>직접 바꾸기 전에 새 버전 실행에 쓰는 형식을 불러 둔다 (바꾼 뒤에는 exe 에서 새 코드를 못 읽을 수 있다)</summary>
    private static void PrewarmRelaunch()
    {
        RuntimeHelpers.RunClassConstructor(typeof(Process).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ProcessStartInfo).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(System.ComponentModel.Win32Exception).TypeHandle);
    }

    /// <summary>
    /// 앱의 Main 이 맨 먼저 부른다: --finish-update 로 불렸으면 파일을 바꾸고 새 버전을 띄운 뒤 끝낼 종료 코드, 아니면 null(평소 시작).
    /// 화면·단일 실행 잠금·세이브는 건드리지 않는다.
    /// </summary>
    public static int? RunFinishUpdate(string[] args)
    {
        int at = Array.IndexOf(args, FinishUpdateArg);
        if (at < 0) return null;
        if (args.Length < at + 6)
        {
            AppLog.Warn("업데이트 마무리: 인자가 모자라요");
            return 2;
        }
        var kind = args[at + 1] == "app" ? Kind.MacBundle : Kind.WindowsExe;
        string installed = args[at + 2], source = args[at + 3], ready = args[at + 5];
        int.TryParse(args[at + 4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int pid);
        try { File.WriteAllText(ready, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"업데이트 마무리: 준비 신호를 못 남김: {ex.Message}");
            return 3; // 옛 앱은 신호가 없으니 직접 바꾸기로 간다
        }
        return Finish(kind, installed, source, () => WaitForExit(pid, TimeSpan.FromSeconds(60)), path => Relaunch(kind, path)) ? 0 : 1;
    }

    /// <summary>
    /// 마무리 본체: 옛 프로세스가 끝나길 기다림 → 설치 자리의 옛 것을 .old 로 → 새 것을 그 자리로 복사 → 그 자리에서 다시 띄우기.
    /// 옛 프로세스가 안 끝나면 아무것도 바꾸지 않는다. 복사가 실패하면 옛 것을 되돌려 놓고 그것을 다시 띄운다 (앱이 사라지지 않게).
    /// </summary>
    internal static bool Finish(Kind kind, string installed, string source, Func<bool> waitForOldExit, Func<string, bool> relaunch)
    {
        if (!waitForOldExit())
        {
            AppLog.Warn("업데이트 마무리: 옛 앱이 끝나지 않아 그만둬요 (다음에 다시 받아요)");
            return false;
        }
        bool mac = kind == Kind.MacBundle;
        var old = installed + ".old";
        try
        {
            if (mac)
            {
                if (Directory.Exists(old)) Directory.Delete(old, true);
                Directory.Move(installed, old);
            }
            else
            {
                try { if (File.Exists(old)) File.Delete(old); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { old = $"{installed}.{Guid.NewGuid():N}.old"; }
                Retry(() => File.Move(installed, old)); // 백신이 막 끝난 exe 를 잠깐 잡고 있을 수 있다
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"업데이트 마무리: 옛 버전을 옮기지 못해 그만둬요 ({ex.GetType().Name}): {ex.Message}");
            relaunch(installed);
            return false;
        }

        try
        {
            if (mac) Run("ditto", source, installed);
            else Retry(() => File.Copy(source, installed, overwrite: true)); // 중간에 끊긴 복사가 남아 있어도 다시
        }
        catch (Exception ex)
        {
            AppLog.Warn($"업데이트 마무리: 새 버전을 놓지 못해 되돌려요 ({ex.GetType().Name}): {ex.Message}");
            try
            {
                if (mac) { if (Directory.Exists(installed)) Directory.Delete(installed, true); Directory.Move(old, installed); }
                else { if (File.Exists(installed)) File.Delete(installed); File.Move(old, installed); }
            }
            catch (Exception restore) { AppLog.Error("업데이트 마무리: 되돌리기 실패", restore); }
            relaunch(installed);
            return false;
        }

        AppLog.Info($"업데이트 마무리: {installed} 를 새 버전으로 바꿈");
        return relaunch(installed);
    }

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        if (pid <= 0) return true;
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.WaitForExit(timeout);
        }
        catch (ArgumentException) { return true; } // 이미 끝남
    }

    private static void Retry(Action action)
    {
        for (int i = 0; ; i++)
        {
            try { action(); return; }
            // 잠김(공유 위반·접근 거부)만 잠깐 뒤 다시 — 없는 파일은 기다려도 소용없다
            catch (Exception ex) when (ex is (IOException and not FileNotFoundException and not DirectoryNotFoundException) or UnauthorizedAccessException
                                       && i < 20) { Thread.Sleep(250); }
        }
    }

    /// <summary>지난 업데이트가 남긴 것(옛 exe·옛 .app·받은 파일) 지우기 — 시작할 때 한 번, 실패는 다음 기회에</summary>
    public void CleanupLeftovers()
    {
        try
        {
            if (_processPath != null && InstallKind == Kind.WindowsExe && Path.GetDirectoryName(_processPath) is { } dir)
                foreach (var f in Directory.GetFiles(dir, Path.GetFileName(_processPath) + ".*old")) TryDelete(f);
            if (_processPath != null && InstallKind == Kind.MacBundle && BundleOf(_processPath) is { } bundle && Directory.Exists(bundle + ".old"))
                Directory.Delete(bundle + ".old", true);
            if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn($"지난 업데이트 정리 실패: {ex.Message}"); }

        static void TryDelete(string f)
        {
            try { File.Delete(f); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static Kind DetectKind(string? processPath)
    {
        if (processPath == null) return Kind.Unsupported;
        if (OperatingSystem.IsWindows() && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Kind.WindowsExe;
        if (OperatingSystem.IsMacOS() && BundleOf(processPath) != null) return Kind.MacBundle;
        return Kind.Unsupported;
    }

    private Blocker FindBlocker()
    {
        // 개발 빌드(dotnet run·bin 폴더)는 배포판이 없다 — 받은 단일 exe 로 덮어쓰면 개발 환경이 꼬이니 알리기만 한다
        if (_flavor == null) return Blocker.DevBuild;
        if (_processPath == null || InstallKind == Kind.Unsupported) return Blocker.Environment;
        if ((InstallKind == Kind.WindowsExe) != _flavor.StartsWith("win-", StringComparison.Ordinal)) return Blocker.WrongOs;
        string? folder;
        if (InstallKind == Kind.MacBundle)
        {
            if (_processPath.Contains("/AppTranslocation/", StringComparison.Ordinal)) return Blocker.Translocated;
            folder = Path.GetDirectoryName(BundleOf(_processPath));
        }
        else folder = Path.GetDirectoryName(_processPath);
        return folder != null && CanWrite(folder) ? Blocker.None : Blocker.ReadOnly;
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".cozyorbit-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>…/X.app/Contents/MacOS/실행파일 → …/X.app</summary>
    internal static string? BundleOf(string processPath)
    {
        var macOs = Path.GetDirectoryName(processPath);
        var contents = macOs == null ? null : Path.GetDirectoryName(macOs);
        var bundle = contents == null ? null : Path.GetDirectoryName(contents);
        return bundle != null && Path.GetFileName(macOs) == "MacOS" && Path.GetFileName(contents) == "Contents"
            && bundle.EndsWith(".app", StringComparison.Ordinal) ? bundle : null;
    }

    private static bool Relaunch(Kind kind, string path)
    {
        try
        {
            var psi = kind == Kind.MacBundle
                ? new ProcessStartInfo("open") { ArgumentList = { "-n", path, "--args", AfterUpdateArg } }
                : new ProcessStartInfo(path) { ArgumentList = { AfterUpdateArg }, WorkingDirectory = Path.GetDirectoryName(path) ?? "" };
            psi.UseShellExecute = false;
            return Process.Start(psi) != null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"새 버전 실행 실패: {ex.Message}");
            return false;
        }
    }

    private static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new UpdateInstallException($"{file} did not start");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new UpdateInstallException($"{file} failed ({p.ExitCode}): {err.Trim()}");
    }
}

public sealed class UpdateInstallException(string message) : Exception(message);
