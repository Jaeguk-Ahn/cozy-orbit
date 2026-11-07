namespace CozyOrbit.Core.Game;

/// <summary>
/// 밸런스 수치 한 곳 (docs/design.md §3~§6 과 같아야 한다 — 바꾸면 문서도 함께).
/// </summary>
public static class Balance
{
    // ── 토큰 → 별빛 (§3) ──
    public const double WeightOutput = 1.0;
    public const double WeightInput = 0.25;
    public const double WeightCacheWrite = 0.25;
    public const double WeightCacheRead = 0.01;

    /// <summary>하루 별빛 곡선의 크기 (초)</summary>
    public const double DailyStarlightScale = 1800;
    /// <summary>하루 별빛 곡선의 기준 가중 토큰</summary>
    public const double DailyStarlightPivot = 100_000;

    /// <summary>배터리가 가득 차 못 담은 별빛 1 → 별가루</summary>
    public const double OverflowDustPerStarlight = 0.5;

    /// <summary>스캔 하한을 지금보다 이만큼 이전으로는 내리지 않는다</summary>
    public static readonly TimeSpan MaxScanBack = TimeSpan.FromDays(7);
    /// <summary>장부에 남기는 날짜 수</summary>
    public const int LedgerKeepDays = 14;

    // ── 시뮬레이션 (§4) ──
    public const double BaseSpeed = 1;
    public const double BaseBoostMultiplier = 6;
    public const double DustPerStep = 0.1;
    public const double DustPerCrewPerSecond = 0.2;
    public const double BaseBatteryCapacity = 3600;
    public static readonly TimeSpan BaseOfflineCap = TimeSpan.FromHours(8);

    // ── 연구 효과/레벨 (§5) — 비용은 Upgrades 카탈로그 ──
    // 엔진·망원경 배율의 곱(1.1 × 1.1)이 비용 성장(1.6·1.65)보다 한참 작아야 폭주하지 않는다
    public const double EnginePerLevel = 1.1;
    public const double BatteryPerLevel = 1800;
    /// <summary>태양 돛은 토큰 없이도 조금 부스트하게 해 주는 정도 (최대 10레벨 = 하루 약 43분)</summary>
    public const double SailPerLevel = 0.003;
    public const double BoosterPerLevel = 1;
    public const double ScopePerLevel = 1.1;
    public const double BunkHoursPerLevel = 2;

    // ── 발견 (§6) ──
    // 거리는 다항식: 속도가 다항식으로 자라므로 지수식이면 후반 간격이 너무 벌어지거나 폭주한다
    public const double DiscoveryBaseDistance = 30;
    public const double DiscoveryDistancePower = 5;
    public const double DiscoveryBaseReward = 20;
    public const double DiscoveryRewardPower = 2;

    /// <summary>k 번째(0부터) 발견이 일어나는 누적 거리 = 30 + (k+1)^5</summary>
    public static double DiscoveryDistance(int k) =>
        Math.Round(DiscoveryBaseDistance + Math.Pow(k + 1, DiscoveryDistancePower), MidpointRounding.AwayFromZero);

    /// <summary>k 번째(0부터) 발견의 별가루 보상 = 20 × (k+1)^2</summary>
    public static double DiscoveryReward(int k) =>
        Math.Round(DiscoveryBaseReward * Math.Pow(k + 1, DiscoveryRewardPower), MidpointRounding.AwayFromZero);

    /// <summary>가중 토큰 하루치 → 별빛 (로그 곡선, 단조 증가)</summary>
    public static double DailyStarlight(double weightedTokens) =>
        weightedTokens <= 0 ? 0 : DailyStarlightScale * Math.Log(1 + weightedTokens / DailyStarlightPivot);

    /// <summary><see cref="DailyStarlight"/> 의 역함수: 하루 별빛 → 필요한 가중 토큰</summary>
    public static double WeightedForDailyStarlight(double starlight) =>
        starlight <= 0 ? 0 : DailyStarlightPivot * (Math.Exp(starlight / DailyStarlightScale) - 1);
}
