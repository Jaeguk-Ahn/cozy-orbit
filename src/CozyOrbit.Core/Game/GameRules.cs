namespace CozyOrbit.Core.Game;

/// <summary>발견 한 건 (시뮬레이션 결과). Id 는 도감 천체 id (이름 없는 별이면 null).</summary>
public sealed record DiscoveryEvent(int Index, string? Id, string Name, string Sprite, double Reward);

/// <summary>한 번의 진행 결과</summary>
public sealed class TickResult
{
    public double Seconds { get; set; }
    public double DistanceGained { get; set; }
    public double StardustGained { get; set; }
    public double BoostSeconds { get; set; }
    public List<DiscoveryEvent> Discoveries { get; } = new();

    public void Add(TickResult other)
    {
        Seconds += other.Seconds;
        DistanceGained += other.DistanceGained;
        StardustGained += other.StardustGained;
        BoostSeconds += other.BoostSeconds;
        Discoveries.AddRange(other.Discoveries);
    }
}

/// <summary>
/// 게임 규칙 (docs/design.md §4·§5). 상태를 받아 결정적으로 진행한다 — 시계·난수 없음.
/// </summary>
public static class GameRules
{
    // ── 연구 효과 ──
    public static double EngineMultiplier(int level) => Math.Pow(Balance.EnginePerLevel, level);
    public static double BatteryCapacity(int level) => Balance.BaseBatteryCapacity + Balance.BatteryPerLevel * level;
    public static double SailPerSecond(int level) => Balance.SailPerLevel * level;
    public static double BoostMultiplier(int level) => Balance.BaseBoostMultiplier + Balance.BoosterPerLevel * level;
    public static double DustMultiplier(int level) => Math.Pow(Balance.ScopePerLevel, level);
    public static TimeSpan OfflineCap(int level) => Balance.BaseOfflineCap + TimeSpan.FromHours(Balance.BunkHoursPerLevel * level);

    /// <summary>끝낸 항해에서 얻은 영구 배율 (속도·별가루 모두에)</summary>
    public static double VoyageMultiplier(GameState s) => Math.Pow(Balance.VoyageBonus, Math.Max(0, s.Voyage));

    public static double Speed(GameState s) => Balance.BaseSpeed * EngineMultiplier(s.Level(Upgrades.Engine)) * VoyageMultiplier(s);
    public static double BatteryCapacity(GameState s) => BatteryCapacity(s.Level(Upgrades.Battery));
    public static double BoostMultiplier(GameState s) => BoostMultiplier(s.Level(Upgrades.Booster));
    public static int CrewCount(GameState s) => s.Level(Upgrades.Crew);
    public static TimeSpan OfflineCap(GameState s) => OfflineCap(s.Level(Upgrades.Bunk));

    /// <summary>지금 부스트가 걸리는지</summary>
    public static bool IsBoosting(GameState s) => (s.Settings.AutoBoost || s.ManualBoost) && s.Starlight > 0;

    /// <summary>지금 실제 속도 (부스트 포함, 별걸음/초)</summary>
    public static double CurrentSpeed(GameState s) => Speed(s) * (IsBoosting(s) ? BoostMultiplier(s) : 1);

    /// <summary>지금 별가루 배율 (망원경 × 항해 배율)</summary>
    public static double DustMultiplier(GameState s) => DustMultiplier(s.Level(Upgrades.Scope)) * VoyageMultiplier(s);

    /// <summary>초당 별가루 (지금 속도 기준)</summary>
    public static double DustPerSecond(GameState s) =>
        CurrentSpeed(s) * Balance.DustPerStep * DustMultiplier(s)
        + CrewCount(s) * Balance.DustPerCrewPerSecond * VoyageMultiplier(s);

    public static double NextDiscoveryDistance(GameState s) => Balance.DiscoveryDistance(s.Voyage, s.DiscoveryCount);

    /// <summary>이번 항해의 직전 발견 거리 (진행 막대의 시작점)</summary>
    public static double PrevDiscoveryDistance(GameState s) =>
        s.DiscoveryCount == 0 ? 0 : Balance.DiscoveryDistance(s.Voyage, s.DiscoveryCount - 1);

    // ── 진행 ──

    /// <summary>seconds 만큼 진행한다. 큰 값은 1초 단위로 나눠 부스트 소진·발견 순서를 정확히 맞춘다.</summary>
    public static TickResult Advance(GameState s, double seconds)
    {
        var result = new TickResult();
        if (!(seconds > 0)) return result;
        double left = seconds;
        while (left > 0)
        {
            double dt = Math.Min(1.0, left);
            Step(s, dt, result);
            left -= dt;
        }
        s.Stats.PlaySeconds += seconds;
        return result;
    }

    private static void Step(GameState s, double dt, TickResult r)
    {
        double cap = BatteryCapacity(s);
        double sail = SailPerSecond(s.Level(Upgrades.Sail));
        if (sail > 0 && s.Starlight < cap) s.Starlight = Math.Min(cap, s.Starlight + sail * dt);

        double boost = 0;
        if ((s.Settings.AutoBoost || s.ManualBoost) && s.Starlight > 0)
        {
            boost = Math.Min(dt, s.Starlight);
            s.Starlight -= boost;
            if (s.Starlight <= 1e-9) s.Starlight = 0;
        }
        if (s.ManualBoost && s.Starlight <= 0) s.ManualBoost = false;

        double speed = Speed(s);
        double advance = speed * (dt + boost * (BoostMultiplier(s) - 1));
        double dust = advance * Balance.DustPerStep * DustMultiplier(s)
                      + CrewCount(s) * Balance.DustPerCrewPerSecond * VoyageMultiplier(s) * dt;

        s.Distance += advance;
        s.Stats.DistanceTotal += advance;
        AddDust(s, dust);
        r.Seconds += dt;
        r.DistanceGained += advance;
        r.StardustGained += dust;
        r.BoostSeconds += boost;
        s.Stats.BoostSeconds += boost;

        var dest = Destinations.Current(s);
        while (s.Distance >= NextDiscoveryDistance(s))
        {
            int k = s.DiscoveryCount;
            double reward = Balance.DiscoveryReward(s.Voyage, k);
            if (Destinations.At(dest, k) is { } found) s.FoundAt.TryAdd(found.Id, s.LastTick);
            s.DiscoveryCount++;
            s.Stats.Discoveries++;
            AddDust(s, reward);
            r.StardustGained += reward;
            r.Discoveries.Add(new DiscoveryEvent(k, Destinations.At(dest, k)?.Id, Destinations.NameOf(dest, k), Destinations.SpriteOf(dest, k), reward));
        }
    }

    internal static void AddDust(GameState s, double amount)
    {
        if (!(amount > 0)) return;
        s.Stardust += amount;
        s.Stats.StardustEarned += amount;
    }

    /// <summary>
    /// 마지막 진행 이후 now 까지 진행한다. 오프라인 상한을 넘는 시간은 버린다. 시계가 뒤로 갔으면 진행 없이 기준만 옮긴다.
    /// </summary>
    public static TickResult CatchUp(GameState s, DateTimeOffset now)
    {
        var elapsed = now - s.LastTick;
        if (elapsed <= TimeSpan.Zero)
        {
            s.LastTick = now;
            return new TickResult();
        }
        var cap = OfflineCap(s);
        if (elapsed > cap) elapsed = cap;
        // 발견 시각은 진행 중의 LastTick 을 쓰므로 실제 지금으로 맞춰 둔다 (오프라인 발견은 "돌아온 시각"으로 기록)
        s.LastTick = now;
        return Advance(s, elapsed.TotalSeconds);
    }

    // ── 연구 ──

    public static double CostOf(GameState s, UpgradeDef u) => u.CostAt(s.Level(u.Id));

    public static bool CanBuy(GameState s, UpgradeDef u) => !u.IsMaxed(s.Level(u.Id)) && s.Stardust >= CostOf(s, u);

    public static bool TryBuy(GameState s, string id)
    {
        var u = Upgrades.Get(id);
        if (!CanBuy(s, u)) return false;
        s.Stardust -= CostOf(s, u);
        s.Upgrades[id] = s.Level(id) + 1;
        s.Stats.ResearchBought++;
        return true;
    }

    /// <summary>수동 부스트 토글. 별빛이 없으면 켜지지 않는다.</summary>
    public static void SetManualBoost(GameState s, bool on) => s.ManualBoost = on && s.Starlight > 0;

    // ── 우주 이벤트·햄찌 (§12) ──

    /// <summary>별똥별 하나의 별가루 = 지금 수입 45초치 (최소 10). 유성우의 작은 별똥은 이것의 1/4.</summary>
    public static double ShootingStarReward(GameState s, bool small = false)
    {
        double reward = Math.Max(Balance.ShootingStarMinReward, Math.Round(DustPerSecond(s) * Balance.ShootingStarSeconds));
        return small ? Math.Max(1, Math.Round(reward / 4)) : reward;
    }

    /// <summary>별똥별을 잡았다: 별가루 + 기록. 받은 별가루를 돌려준다.</summary>
    public static double CatchShootingStar(GameState s, bool small = false)
    {
        double reward = ShootingStarReward(s, small);
        AddDust(s, reward);
        s.Stats.ShootingStars++;
        return reward;
    }

    /// <summary>햄찌를 쓰다듬었다 (기록만 — 목표·도전과제가 센다)</summary>
    public static void Pet(GameState s) => s.Stats.Pets++;
}
