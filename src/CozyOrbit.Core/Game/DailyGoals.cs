using System.Globalization;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>
/// 오늘의 목표 하나의 정의. 진행 = Counter(지금) − Baseline(뽑을 때의 누계) 이 Target 에 닿으면 완료.
/// Title·Amount 는 목표치/양을 받아 현재 언어로 적는다. Available 이 false 인 날에는 뽑히지 않는다 (해낼 수 없는 목표 방지).
/// Usage 목표(AI 사용량이 있어야 하는 것)는 하루에 하나만 — 토큰을 안 쓰는 날도 마음 편하게.
/// </summary>
public sealed record GoalDef(
    string Id,
    string Icon,
    Func<double, string> Title,
    Func<double, string> Amount,
    Func<GameState, double> Counter,
    Func<GameState, double> Target,
    int Reward,
    bool Usage = false,
    Func<GameState, bool>? Available = null);

/// <summary>
/// 오늘의 목표 (하루 세 가지, 별사탕 보상). 날짜로 씨앗을 정해 같은 날엔 언제 열어도 같은 목표가 나온다.
/// 진행은 기존 누계(GameStats·CandyState)만 읽는다 — 목표 때문에 따로 세는 값은 없다.
/// </summary>
public static class DailyGoals
{
    public const int GoalsPerDay = 3;
    /// <summary>세 가지를 다 받았을 때의 덤</summary>
    public const int BonusReward = 10;

    /// <summary>이동 목표는 지금 기본 속도로 이만큼(초) 가는 거리</summary>
    private const double TravelSeconds = 25 * 60;
    /// <summary>발견 목표는 다음 발견이 지금 속도로 이 시간(초) 안일 때만</summary>
    private const double DiscoverWithinSeconds = 3 * 3600;

    public static readonly IReadOnlyList<GoalDef> All = new GoalDef[]
    {
        new("travel", "icon_distance",
            t => L.T($"{Fmt.Steps(t)} 여행하기", $"Travel {Fmt.Steps(t)}"),
            Count,
            s => s.Stats.DistanceTotal,
            s => Nice(GameRules.Speed(s) * TravelSeconds),
            10),
        new("discover", "icon_book",
            t => L.T($"새 천체 {t:0}개 만나기", $"Discover {t:0} new object" + (t > 1 ? "s" : "")),
            Count,
            s => s.Stats.Discoveries,
            _ => 1,
            10,
            Available: s => (GameRules.NextDiscoveryDistance(s) - s.Distance) / GameRules.Speed(s) <= DiscoverWithinSeconds),
        new("research", "icon_lab",
            t => L.T($"연구 {t:0}레벨 올리기", $"Buy {t:0} research levels"),
            Count,
            s => s.Stats.ResearchBought,
            _ => 2,
            8,
            Available: s => ResearchLevelsLeft(s) >= 2),
        new("pet", "icon_crew",
            t => L.T($"햄찌 {t:0}번 쓰다듬기", $"Pet the hamsters {t:0} times"),
            Count,
            s => s.Stats.Pets,
            _ => 5,
            5),
        new("shooting_star", "fx_shooting_star",
            t => L.T($"별똥별 {t:0}개 잡기", $"Catch {t:0} shooting stars"),
            Count,
            s => s.Stats.ShootingStars,
            _ => 2,
            8),
        new("boost", "icon_booster",
            t => L.T($"{Fmt.Duration(t)} 부스트하기", $"Boost for {Fmt.Duration(t)}"),
            Fmt.Duration,
            s => s.Stats.BoostSeconds,
            _ => 20 * 60,
            8,
            Usage: true),
        new("starlight", "icon_starlight",
            t => L.T($"별빛 {Fmt.Duration(t)}어치 받기", $"Receive {Fmt.Duration(t)} of starlight"),
            Fmt.Duration,
            s => s.Stats.StarlightEarned,
            _ => 30 * 60,
            12,
            Usage: true),
        new("play", "icon_bunk",
            t => L.T($"오늘 {Fmt.Duration(t)} 켜 두기", $"Keep the game running {Fmt.Duration(t)} today"),
            Fmt.Duration,
            PlayedTodaySeconds,
            _ => 2 * 3600,
            10),
        new("candy", "icon_candy",
            t => L.T($"켜 둔 동안 별사탕 {t:0}개 모으기", $"Earn {t:0} star candy while running"),
            Count,
            CandyFromUsageToday,
            _ => 10,
            15,
            Usage: true),
    };

    public static GoalDef? Find(string id) => All.FirstOrDefault(g => g.Id == id);

    /// <summary>날이 바뀌었으면 오늘의 목표를 새로 뽑는다. 새로 뽑았으면 true (화면이 목록을 다시 만든다).</summary>
    public static bool Ensure(GameState s, DateTimeOffset now)
    {
        var day = StarlightLedger.DayKey(now);
        if (s.Daily.Day == day && s.Daily.Goals.Count > 0) return false;

        // Day 를 먼저 적는다 — "오늘 켜 둔 시간" 같은 누계가 오늘 날짜를 기준으로 센다
        s.Daily.Day = day;
        s.Daily.BonusClaimed = false;
        s.Daily.Goals = Pick(s, day)
            .Select(g => new DailyGoal { Id = g.Id, Target = g.Target(s), Baseline = g.Counter(s) })
            .ToList();
        return true;
    }

    /// <summary>날짜 씨앗으로 섞은 순서대로, 오늘 해낼 수 있는 것만 세 개 (사용량 목표는 하나까지)</summary>
    private static List<GoalDef> Pick(GameState s, string day)
    {
        var order = Enumerable.Range(0, All.Count).ToArray();
        ulong state = Seed(day);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = (int)(Next(ref state) % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        var picked = new List<GoalDef>(GoalsPerDay);
        bool usage = false;
        foreach (int i in order)
        {
            var g = All[i];
            if (g.Usage && usage) continue;
            if (g.Available != null && !g.Available(s)) continue;
            picked.Add(g);
            usage |= g.Usage;
            if (picked.Count == GoalsPerDay) break;
        }
        return picked;
    }

    /// <summary>날짜 문자열 → 64비트 씨앗 (FNV-1a). string.GetHashCode 는 실행마다 달라 쓰지 않는다.</summary>
    private static ulong Seed(string day)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in day)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>splitmix64 — 작고 결정적인 난수</summary>
    private static ulong Next(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // ── 진행 ──

    /// <summary>오늘 쌓인 양 (0 이상)</summary>
    public static double Current(GameState s, DailyGoal goal) =>
        Find(goal.Id) is { } def ? Math.Max(0, def.Counter(s) - goal.Baseline) : 0;

    /// <summary>0..1</summary>
    public static double Progress(GameState s, DailyGoal goal)
    {
        if (Find(goal.Id) == null) return 0;
        if (goal.Target <= 0) return 1;
        return Math.Clamp(Current(s, goal) / goal.Target, 0, 1);
    }

    public static bool IsDone(GameState s, DailyGoal goal) => Find(goal.Id) != null && Current(s, goal) >= goal.Target - 1e-9;

    public static bool CanClaim(GameState s, DailyGoal goal) => !goal.Claimed && IsDone(s, goal);

    /// <summary>오늘 목표의 보상을 받는다 (완료했고 아직 안 받았을 때만)</summary>
    public static bool TryClaim(GameState s, string id)
    {
        var goal = s.Daily.Goals.FirstOrDefault(g => g.Id == id);
        if (goal == null || !CanClaim(s, goal)) return false;
        goal.Claimed = true;
        AddCandy(s, Find(id)!.Reward);
        return true;
    }

    public static bool AllClaimed(GameState s) => s.Daily.Goals.Count > 0 && s.Daily.Goals.All(g => g.Claimed);

    public static bool CanClaimBonus(GameState s) => !s.Daily.BonusClaimed && AllClaimed(s);

    /// <summary>세 가지를 다 받았을 때의 덤</summary>
    public static bool TryClaimBonus(GameState s)
    {
        if (!CanClaimBonus(s)) return false;
        s.Daily.BonusClaimed = true;
        AddCandy(s, BonusReward);
        return true;
    }

    internal static void AddCandy(GameState s, double amount)
    {
        s.Candy.Balance += amount;
        s.Candy.Earned += amount;
    }

    // ── 누계 도우미 ──

    /// <summary>오늘(Daily.Day 의 로컬 하루) 안에 켜 둔 시간 합 (초) — 켜 둔 구간 기록에서 오늘과 겹치는 부분만</summary>
    public static double PlayedTodaySeconds(GameState s)
    {
        if (DayStart(s) is not { } start) return 0;
        double total = 0;
        foreach (var x in s.Candy.Sessions)
        {
            var from = x.Start > start ? x.Start : start;
            if (x.End > from) total += (x.End - from).TotalSeconds;
        }
        return total;
    }

    /// <summary>
    /// 오늘과 겹치는 켜 둔 구간에서 사용량으로 받은 별사탕 합. Candy.Earned 는 목표·도전과제 보상도 늘어나므로 쓰지 않는다
    /// (보상을 받으면 별사탕 목표가 저절로 차는 일이 없게). 자정 전 몫은 Baseline 이 뺀다.
    /// </summary>
    public static double CandyFromUsageToday(GameState s)
    {
        if (DayStart(s) is not { } start) return 0;
        double weighted = 0;
        foreach (var x in s.Candy.Sessions)
            if (x.End >= start) weighted += x.CreditedWeighted;
        return weighted / CandyLedger.WeightedPerCandy;
    }

    private static DateTimeOffset? DayStart(GameState s)
    {
        if (!DateTime.TryParseExact(s.Daily.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    private static int ResearchLevelsLeft(GameState s)
    {
        long left = 0;
        foreach (var u in Upgrades.All)
            left += u.MaxLevel is int max ? Math.Max(0, max - s.Level(u.Id)) : int.MaxValue;
        return (int)Math.Min(left, int.MaxValue);
    }

    private static string Count(double v) => Fmt.Compact(v);

    /// <summary>앞 두 자리만 남기고 올림 (1234 → 1300) — 목표치가 깔끔해 보이게. 최소 100.</summary>
    internal static double Nice(double v)
    {
        if (!(v > 100)) return 100;
        double unit = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);
        return Math.Round(Math.Ceiling(v / unit - 1e-9) * unit);
    }
}
