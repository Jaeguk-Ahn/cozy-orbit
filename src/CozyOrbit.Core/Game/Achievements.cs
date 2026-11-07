using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>
/// 도전과제 하나 (영구). Counter(지금 누계)가 Target 에 닿으면 받을 수 있다.
/// Target 은 함수 — 목적지·스킨 목록처럼 다른 카탈로그 크기를 읽는 것은 처음 쓸 때 계산한다.
/// </summary>
public sealed record AchievementDef(
    string Id,
    string Icon,
    string NameKo, string NameEn,
    string DescKo, string DescEn,
    Func<GameState, double> Counter,
    Func<double> Target,
    int Reward,
    Func<double, string> Amount)
{
    public string Name => L.T(NameKo, NameEn);
    public string Description => L.T(DescKo, DescEn);
}

/// <summary>도전과제 목록 + 진행·보상 (보상 = 별사탕, 한 번만 — 받은 id 는 GameState.Achievements)</summary>
public static class Achievements
{
    public static readonly IReadOnlyList<AchievementDef> All = new AchievementDef[]
    {
        // ── 거리 (모든 항해 합) ──
        Steps("distance_10k", 10_000, "첫 산책", "First Stroll", 10),
        Steps("distance_1m", 1_000_000, "먼 길", "Long Way", 30),
        Steps("distance_100m", 100_000_000, "은하 횡단", "Galaxy Crossing", 80),
        Steps("distance_10b", 10_000_000_000, "끝없는 항해", "Endless Voyage", 150),

        // ── 발견 (모든 항해 합) ──
        Discover(10, "꼬마 탐험가", "Little Explorer", 15),
        Discover(30, "별 수집가", "Star Collector", 40),
        Discover(60, "우주 박물학자", "Cosmic Naturalist", 80),
        Discover(120, "도감 박사", "Atlas Doctor", 150),
        Atlas(Destinations.MilkyWay, 100),
        Atlas(Destinations.Andromeda, 60),
        Atlas(Destinations.Magellanic, 60),
        Atlas(Destinations.VirgoCluster, 60),
        Atlas(Destinations.DeepField, 60),
        Atlas(Destinations.CosmicWeb, 60),
        new("sectors_10", "icon_goal", "구역 탐험가", "Sector Explorer",
            "구역 10곳의 천체를 모두 만나기", "Complete 10 sectors",
            s => s.Stats.SectorsCompleted, () => 10, 40, Fmt.Compact),
        new("sectors_all", "icon_goal", "지도 위의 모든 곳", "Every Corner of the Map",
            "정해진 목적지의 구역을 모두 채우기", "Complete every sector of every charted destination",
            s => s.Stats.SectorsCompleted, () => Destinations.All.Sum(d => d.Sectors.Count), 150, Fmt.Compact),

        // ── 항해 ──
        new("voyage_first", "icon_voyage", "새 항해", "New Voyage",
            "처음으로 다음 목적지를 향해 떠나기", "Set off for the next destination",
            s => s.Voyage, () => 1, 50, Fmt.Compact),
        new("voyage_all", "dest_cosmic_web", "우주 일주", "Around the Universe",
            "정해진 목적지를 모두 지나기", "Reach every charted destination",
            s => s.Voyage, () => Math.Max(1, Destinations.All.Count - 1), 200, Fmt.Compact),

        // ── AI 토큰 (가중) ──
        Tokens("tokens_1m", 1_000_000, "든든한 동료", "Trusty Crewmate", 20),
        Tokens("tokens_10m", 10_000_000, "별빛 발전소", "Starlight Plant", 60),
        Tokens("tokens_100m", 100_000_000, "은하의 동력", "Galactic Power", 150),

        // ── 별사탕 ──
        Candy("candy_100", 100, "달콤한 시작", "Sweet Start", 20),
        Candy("candy_1000", 1000, "사탕 부자", "Candy Tycoon", 80),

        // ── 연구 (산 레벨 합, 모든 항해) ──
        Research("research_50", 50, "꼬마 연구원", "Junior Researcher", 20),
        Research("research_300", 300, "햄찌 박사님", "Professor Hamster", 80),

        // ── 햄찌·우주 이벤트 ──
        new("pets_100", "icon_crew", "쓰담쓰담", "Pat Pat",
            "햄찌를 100번 쓰다듬기", "Pet the hamsters 100 times",
            s => s.Stats.Pets, () => 100, 30, Fmt.Compact),
        new("shooting_stars_25", "fx_shooting_star", "소원 25개", "Twenty-Five Wishes",
            "별똥별 25개 잡기", "Catch 25 shooting stars",
            s => s.Stats.ShootingStars, () => 25, 30, Fmt.Compact),
        new("whale_5", "obj_whale_cloud", "고래 친구", "Whale Friend",
            "우주 고래에게 5번 인사하기", "Wave to the space whale 5 times",
            s => s.Stats.WhaleGreets, () => 5, 20, Fmt.Compact),
        new("whale_30", "obj_whale_cloud", "고래와 단짝", "Whale Buddy",
            "우주 고래에게 30번 인사하기", "Wave to the space whale 30 times",
            s => s.Stats.WhaleGreets, () => 30, 60, Fmt.Compact),

        // ── 꾸미기 ──
        new("skins_5", "icon_shop", "멋쟁이", "Fashionista",
            "스킨 5개 모으기", "Own 5 skins",
            s => PaidSkinsOwned(s), () => 5, 30, Fmt.Compact),
        new("skins_all", "icon_shop", "옷장 가득", "Full Wardrobe",
            "상점의 스킨을 모두 모으기", "Own every skin in the shop",
            s => PaidSkinsOwned(s), () => Math.Max(1, Skins.All.Count(x => !x.IsFree)), 120, Fmt.Compact),
        new("decor_6", "icon_decor", "아늑한 연구소", "Cozy Lab",
            "연구소에 소품 6개 놓기", "Place 6 decorations in the lab",
            s => s.PlacedDecor.Count, () => 6, 30, Fmt.Compact),

        // ── 함께한 시간 ──
        Play("play_24h", 24 * 3600, "24시간", "24 hours", "하루 꼬박", "A Whole Day", 30),
        Play("play_7d", 7 * 24 * 3600, "7일", "7 days", "한 주의 친구", "Friend of the Week", 100),
        Streak("streak_7", 7, "일주일 출석", "Week Streak", 40),
        Streak("streak_30", 30, "한 달 내내", "Month Streak", 120),
    };

    public static AchievementDef? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    public static double Progress(GameState s, AchievementDef a)
    {
        double target = a.Target();
        return target <= 0 ? 1 : Math.Clamp(a.Counter(s) / target, 0, 1);
    }

    public static bool IsDone(GameState s, AchievementDef a) => a.Counter(s) >= a.Target() - 1e-9;

    public static bool IsClaimed(GameState s, string id) => s.Achievements.Contains(id);

    public static bool CanClaim(GameState s, AchievementDef a) => !IsClaimed(s, a.Id) && IsDone(s, a);

    /// <summary>다 이룬 도전과제의 별사탕을 받는다 (한 번만)</summary>
    public static bool TryClaim(GameState s, string id)
    {
        if (Find(id) is not { } a || !CanClaim(s, a)) return false;
        s.Achievements.Add(id);
        DailyGoals.AddCandy(s, a.Reward);
        return true;
    }

    /// <summary>지금 받을 수 있는 도전과제 수 (탭 알림용)</summary>
    public static int ClaimableCount(GameState s) => All.Count(a => CanClaim(s, a));

    private static int PaidSkinsOwned(GameState s) => s.OwnedSkins.Count(id => Skins.Find(id) is { IsFree: false });

    // ── 만들기 도우미 ──

    private static AchievementDef Steps(string id, double target, string ko, string en, int reward) =>
        new(id, "icon_distance", ko, en,
            $"모든 항해를 합쳐 {Fmt.Compact(target)} 별걸음 가기", $"Travel {Fmt.Compact(target)} steps across all voyages",
            s => s.Stats.DistanceTotal, () => target, reward, Fmt.Compact);

    private static AchievementDef Discover(int target, string ko, string en, int reward) =>
        new($"discoveries_{target}", "icon_book", ko, en,
            $"천체 {target}개 만나기", $"Make {target} discoveries",
            s => s.Stats.Discoveries, () => target, reward, Fmt.Compact);

    private static AchievementDef Atlas(DestinationDef d, int reward) =>
        new("atlas_" + d.Id, d.Sprite, $"{d.NameKo} 도감 완성", $"{d.NameEn} Atlas",
            $"{d.NameKo}의 천체를 모두 만나기", $"Find every object in the {d.NameEn}",
            s => d.Catalog.Count(x => s.FoundAt.ContainsKey(x.Id)), () => d.Catalog.Count, reward, Fmt.Compact);

    private static AchievementDef Research(string id, int target, string ko, string en, int reward) =>
        new(id, "icon_lab", ko, en,
            $"연구 레벨을 모두 합쳐 {target}번 올리기", $"Buy {target} research levels across all voyages",
            s => s.Stats.ResearchBought, () => target, reward, Fmt.Compact);

    private static AchievementDef Streak(string id, int days, string ko, string en, int reward) =>
        new(id, "icon_goal", ko, en,
            $"{days}일 연속으로 햄찌들 만나러 오기", $"Visit the hamsters {days} days in a row",
            s => s.Stats.BestPlayStreak, () => days, reward, Days);

    private static string Days(double days) => L.T($"{Fmt.Compact(days)}일", $"{Fmt.Compact(days)}d");

    private static AchievementDef Tokens(string id, double target, string ko, string en, int reward) =>
        new(id, "icon_starlight", ko, en,
            $"가중 토큰 {Fmt.Compact(target)}개를 별빛으로 바꾸기", $"Turn {Fmt.Compact(target)} weighted tokens into starlight",
            s => s.Stats.WeightedTokens, () => target, reward, Fmt.Compact);

    private static AchievementDef Candy(string id, double target, string ko, string en, int reward) =>
        new(id, "icon_candy", ko, en,
            $"별사탕 {Fmt.Compact(target)}개 받기", $"Earn {Fmt.Compact(target)} star candy",
            s => s.Candy.Earned, () => target, reward, Fmt.Compact);

    private static AchievementDef Play(string id, double seconds, string spanKo, string spanEn, string ko, string en, int reward) =>
        new(id, "icon_bunk", ko, en,
            $"햄찌들과 {spanKo} 함께하기", $"Spend {spanEn} with the hamsters",
            s => s.Stats.PlaySeconds, () => seconds, reward, Hours);

    private static string Hours(double seconds)
    {
        double h = Math.Floor(seconds / 3600);
        return L.T($"{Fmt.Compact(h)}시간", $"{Fmt.Compact(h)}h");
    }
}
