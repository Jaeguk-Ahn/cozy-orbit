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

    // ── 우주 이벤트 (§12) ──
    /// <summary>별똥별 보상 = 지금 별가루 수입의 이 초만큼</summary>
    public const double ShootingStarSeconds = 45;
    public const double ShootingStarMinReward = 10;

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
    public const double DiscoveryDistancePower = 4.2;
    public const double DiscoveryBaseReward = 20;
    public const double DiscoveryRewardPower = 2;

    // ── 구역 완성 (§6) — 우리 은하 중반(#24~#48)의 긴 발견 간격 사이에 한 번씩 반가운 보상 ──
    /// <summary>구역 완성 별가루 = max(구역 마지막 발견 보상 × 이것, 부스트 없는 별가루 수입 × SectorRewardSeconds)</summary>
    public const double SectorRewardDiscoveries = 3;
    /// <summary>수입이 발견 보상보다 훨씬 커지는 후반에도 의미 있게: 부스트 없는 수입 2시간치</summary>
    public const double SectorRewardSeconds = 2 * 3600;
    /// <summary>구역 완성 별사탕</summary>
    public const double SectorCandy = 10;

    // ── 새 항해 (§11) ──
    /// <summary>끝낸 항해마다 속도·별가루에 곱해지는 영구 배율</summary>
    public const double VoyageBonus = 1.6;
    /// <summary>
    /// 두 번째 항해부터의 발견 거리·보상 배율. 뒤 목적지는 천체가 12개뿐이라 처음부터(k=0) 세면 몇 시간 만에 끝나므로
    /// 우리 은하 후반쯤의 거리에서 시작하게 크게 곱한다 (시뮬레이션: 가벼운 사용 기준 목적지마다 약 10일).
    /// </summary>
    public const double VoyageBaseScale = 600;
    /// <summary>그 뒤 항해마다 더 곱하는 배율 (보너스보다 조금 커서 뒤 목적지일수록 살짝 길다)</summary>
    public const double VoyageDistanceGrowth = 2.0;
    /// <summary>새 항해를 떠날 때 받는 별사탕 = 이것 × (끝낸 항해 수 + 1)</summary>
    public const double VoyageCandyBase = 100;

    /// <summary>voyage 번째 항해의 발견 거리·보상 배율 (첫 항해 1, 그다음부터 600 × 2^(v-1))</summary>
    public static double VoyageScale(int voyage) =>
        voyage <= 0 ? 1 : VoyageBaseScale * Math.Pow(VoyageDistanceGrowth, voyage - 1);

    /// <summary>voyage 번째 항해의 k 번째(0부터) 발견이 일어나는 누적 거리 = 배율 × (30 + (k+1)^4.2)</summary>
    public static double DiscoveryDistance(int voyage, int k) =>
        Math.Round(VoyageScale(voyage) * (DiscoveryBaseDistance + Math.Pow(k + 1, DiscoveryDistancePower)), MidpointRounding.AwayFromZero);

    /// <summary>voyage 번째 항해의 k 번째(0부터) 발견의 별가루 보상 = 배율 × 20 × (k+1)^2</summary>
    public static double DiscoveryReward(int voyage, int k) =>
        Math.Round(VoyageScale(voyage) * DiscoveryBaseReward * Math.Pow(k + 1, DiscoveryRewardPower), MidpointRounding.AwayFromZero);

    /// <summary>가중 토큰 하루치 → 별빛 (로그 곡선, 단조 증가)</summary>
    public static double DailyStarlight(double weightedTokens) =>
        weightedTokens <= 0 ? 0 : DailyStarlightScale * Math.Log(1 + weightedTokens / DailyStarlightPivot);

    /// <summary><see cref="DailyStarlight"/> 의 역함수: 하루 별빛 → 필요한 가중 토큰</summary>
    public static double WeightedForDailyStarlight(double starlight) =>
        starlight <= 0 ? 0 : DailyStarlightPivot * (Math.Exp(starlight / DailyStarlightScale) - 1);
}
