namespace CozyOrbit.Core.Game;

/// <summary>첫 실행 안내 카드 (선장 햄찌의 세 마디). 본 뒤엔 <see cref="GameSettings.OnboardingDone"/> 에 남는다.</summary>
public static class Onboarding
{
    /// <summary>
    /// 이 기능 이전 세이브로 이미 항해해 온 사람에겐 띄우지 않는다 — 불러온 직후 한 번 부른다.
    /// 거리만으로는 정하지 않는다: 안내를 닫기 전에 껐다 켠 새 선장도 다시 볼 수 있게 (발견 전이면 아직 새 선장).
    /// </summary>
    public static void SkipIfAlreadySailing(GameState s)
    {
        if (s.Settings.OnboardingDone) return;
        if (s.DiscoveryCount > 0 || s.Voyage > 0 || s.FoundAt.Count > 0 || s.Stats.Discoveries > 0)
            s.Settings.OnboardingDone = true;
    }
}
