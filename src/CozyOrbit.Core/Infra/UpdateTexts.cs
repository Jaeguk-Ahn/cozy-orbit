namespace CozyOrbit.Core.Infra;

/// <summary>
/// 업데이트 화면 문구 (앱 화면 코드와 문장을 나눠 둔다).
/// </summary>
public static class UpdateTexts
{
    public static string CurrentVersion(UpdateChecker u) =>
        L.T($"지금 버전 v{u.CurrentVersion}", $"Current version v{u.CurrentVersion}")
        + (u.IsPublishedBuild ? "" : L.T(" (개발 빌드)", " (development build)"));

    /// <summary>상태 줄 — 마지막 확인 결과 + (새 버전이 있으면) 설치 진행·실패·설치 못 하는 까닭</summary>
    public static string Status(UpdateChecker u, UpdateInstaller i)
    {
        var status = u.LastStatus switch
        {
            UpdateChecker.Status.Checking => L.T("새 버전을 찾는 중…", "Checking for a new version…"),
            UpdateChecker.Status.UpToDate => L.T("최신 버전이에요.", "You're up to date."),
            UpdateChecker.Status.NoRelease => L.T("아직 공개된 릴리스가 없어요.", "No release has been published yet."),
            UpdateChecker.Status.RateLimited => L.T("GitHub 요청이 잠시 막혔어요. 조금 뒤에 다시 확인할게요.", "GitHub is rate limiting requests. Will try again later."),
            UpdateChecker.Status.Untrusted => L.T("최신 릴리스의 서명을 확인할 수 없어 무시했어요.", "Couldn't verify the latest release's signature, so it was ignored."),
            UpdateChecker.Status.Failed => L.T("확인하지 못했어요 (인터넷 연결을 확인해 주세요).", "Couldn't check (please check your internet connection)."),
            _ => u.Available == null ? L.T("아직 확인하지 않았어요.", "Not checked yet.") : "",
        };
        if (u.Available is not { } r) return status;

        var offer = L.T($"새 버전 v{r.Version} 이 나왔어요!", $"Version v{r.Version} is out!");
        if (u.LastStatus is not (UpdateChecker.Status.Available or UpdateChecker.Status.NotChecked)) offer = status + "\n" + offer;
        if (i.IsBusy)
            return offer + " " + (i.Progress is { } p && p < 1
                ? L.T($"내려받는 중… {Math.Floor(p * 100):0}%", $"Downloading… {Math.Floor(p * 100):0}%")
                : L.T("설치하는 중…", "Installing…"));
        if (i.Error is { } e) return offer + "\n" + e;
        if (i.CanInstall(r)) return offer;
        if (i.UnsupportedReason is { } why) return offer + "\n" + why;
        if (r.Asset == null)
            return offer + "\n" + L.T("이 배포판용 파일이 릴리스에 없어 자동 설치는 못 해요 — 릴리스 페이지에서 받아 주세요.",
                "This build's file isn't in the release, so it can't install automatically — use the release page.");
        return offer;
    }

    public static string CheckNow => L.T("지금 확인", "Check now");
    public static string InstallAndRestart => L.T("설치하고 다시 시작", "Install & restart");
    public static string OpenReleasePage => L.T("릴리스 페이지 열기", "Open release page");
    public static string AutoCheck => L.T("새 버전 자동 확인 (GitHub 릴리스)", "Check for new versions automatically (GitHub releases)");
}
