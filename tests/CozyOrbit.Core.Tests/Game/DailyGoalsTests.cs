using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class DailyGoalsTests
{
    /// <summary>로컬 시각 (목표의 하루는 로컬 날짜 기준)</summary>
    private static DateTimeOffset Local(int month, int day, int hour)
    {
        var t = new DateTime(2026, month, day, hour, 0, 0);
        return new DateTimeOffset(t, TimeZoneInfo.Local.GetUtcOffset(t));
    }

    private static GameState NewGame() => GameState.New(Local(11, 1, 8));

    private static string[] Ids(GameState s) => s.Daily.Goals.Select(g => g.Id).ToArray();

    /// <summary>뽑힌 목표를 알려진 세 개로 바꾼다 (날짜마다 뽑기가 달라 진행·보상 검사는 고정된 목표로)</summary>
    private static void SetGoals(GameState s, params (string Id, double Target)[] goals)
    {
        s.Daily.Goals = goals.Select(g => new DailyGoal
        {
            Id = g.Id,
            Target = g.Target,
            Baseline = DailyGoals.Find(g.Id)!.Counter(s),
        }).ToList();
    }

    [Fact]
    public void SameDayAlwaysGivesTheSameGoals()
    {
        var a = NewGame();
        var b = NewGame();
        Assert.True(DailyGoals.Ensure(a, Local(11, 7, 0)));
        Assert.True(DailyGoals.Ensure(b, Local(11, 7, 23)));
        Assert.Equal(Ids(a), Ids(b));

        var before = a.Daily.Goals.ToList();
        a.Stats.Pets += 3;
        Assert.False(DailyGoals.Ensure(a, Local(11, 7, 15)));   // 같은 날엔 다시 뽑지 않는다
        Assert.Equal(before, a.Daily.Goals);
    }

    [Fact]
    public void EachDayHasThreeDistinctGoalsWithAtMostOneUsageGoal()
    {
        for (int day = 1; day <= 30; day++)
        {
            var s = NewGame();
            DailyGoals.Ensure(s, Local(11, day, 12));
            var defs = s.Daily.Goals.Select(g => DailyGoals.Find(g.Id)!).ToList();
            Assert.Equal(DailyGoals.GoalsPerDay, defs.Count);
            Assert.Equal(defs.Count, defs.Select(d => d.Id).Distinct().Count());
            Assert.True(defs.Count(d => d.Usage) <= 1);
            Assert.All(defs, d => Assert.InRange(d.Reward, 5, 15));
        }
    }

    [Fact]
    public void NewDayPicksNewGoalsAndNewBaselines()
    {
        var s = NewGame();
        DailyGoals.Ensure(s, Local(11, 7, 9));
        var first = Ids(s);
        s.Daily.Goals[0].Claimed = true;
        s.Daily.BonusClaimed = true;

        // 하루 동안 누계가 늘었다
        s.Stats.DistanceTotal += 5000;
        s.Stats.Pets += 4;
        s.Stats.ShootingStars += 1;
        s.Stats.ResearchBought += 2;
        s.Stats.BoostSeconds += 600;
        s.Stats.StarlightEarned += 900;

        Assert.True(DailyGoals.Ensure(s, Local(11, 8, 9)));
        Assert.Equal(StarlightLedger.DayKey(Local(11, 8, 9)), s.Daily.Day);
        Assert.False(s.Daily.BonusClaimed);
        foreach (var g in s.Daily.Goals)
        {
            Assert.False(g.Claimed);
            Assert.Equal(DailyGoals.Find(g.Id)!.Counter(s), g.Baseline);
            Assert.Equal(0, DailyGoals.Progress(s, g));
        }

        // 날짜가 씨앗이라 며칠 안에 다른 조합이 나온다
        bool differs = false;
        for (int day = 8; day <= 14 && !differs; day++)
        {
            var other = NewGame();
            DailyGoals.Ensure(other, Local(11, day, 9));
            differs = !Ids(other).OrderBy(x => x).SequenceEqual(first.OrderBy(x => x));
        }
        Assert.True(differs);
    }

    [Fact]
    public void ProgressClaimAndNoDoubleClaim()
    {
        var s = NewGame();
        DailyGoals.Ensure(s, Local(11, 7, 9));
        SetGoals(s, ("pet", 5), ("shooting_star", 2), ("research", 2));
        var pet = s.Daily.Goals[0];

        s.Stats.Pets += 3;
        Assert.Equal(0.6, DailyGoals.Progress(s, pet), 9);
        Assert.False(DailyGoals.IsDone(s, pet));
        Assert.False(DailyGoals.TryClaim(s, "pet"));
        Assert.Equal(0, s.Candy.Balance);

        s.Stats.Pets += 5;
        Assert.Equal(1, DailyGoals.Progress(s, pet));
        Assert.True(DailyGoals.IsDone(s, pet));
        int reward = DailyGoals.Find("pet")!.Reward;
        Assert.True(DailyGoals.TryClaim(s, "pet"));
        Assert.Equal(reward, s.Candy.Balance);
        Assert.Equal(reward, s.Candy.Earned);
        Assert.True(pet.Claimed);

        Assert.False(DailyGoals.TryClaim(s, "pet"));           // 두 번 받을 수 없다
        Assert.False(DailyGoals.TryClaim(s, "travel"));        // 오늘 목표가 아니다
        Assert.Equal(reward, s.Candy.Balance);
    }

    [Fact]
    public void BonusOnlyAfterAllThreeAreClaimed()
    {
        var s = NewGame();
        DailyGoals.Ensure(s, Local(11, 7, 9));
        SetGoals(s, ("pet", 5), ("shooting_star", 2), ("research", 2));
        s.Stats.Pets += 5;
        s.Stats.ShootingStars += 2;
        s.Stats.ResearchBought += 2;

        Assert.False(DailyGoals.TryClaimBonus(s));
        Assert.True(DailyGoals.TryClaim(s, "pet"));
        Assert.True(DailyGoals.TryClaim(s, "shooting_star"));
        Assert.False(DailyGoals.TryClaimBonus(s));             // 셋 다 완료했어도 받은 것은 둘
        Assert.True(DailyGoals.TryClaim(s, "research"));

        double before = s.Candy.Balance;
        Assert.True(DailyGoals.TryClaimBonus(s));
        Assert.Equal(before + DailyGoals.BonusReward, s.Candy.Balance);
        Assert.False(DailyGoals.TryClaimBonus(s));
        Assert.Equal(before + DailyGoals.BonusReward, s.Candy.Balance);
    }

    [Fact]
    public void RewardsDoNotFillTheCandyGoal()
    {
        var s = NewGame();
        CandyLedger.BeginSession(s.Candy, Local(11, 7, 8));
        DailyGoals.Ensure(s, Local(11, 7, 9));
        SetGoals(s, ("candy", 10), ("pet", 5), ("shooting_star", 2));
        var candy = s.Daily.Goals[0];

        s.Stats.Pets += 5;
        DailyGoals.TryClaim(s, "pet");
        Assert.Equal(0, DailyGoals.Progress(s, candy));

        // 켜 둔 동안 쓴 토큰으로 받은 별사탕만 센다
        s.Candy.Sessions[^1].CreditedWeighted += 5 * CandyLedger.WeightedPerCandy;
        Assert.Equal(0.5, DailyGoals.Progress(s, candy), 9);
    }

    [Fact]
    public void PlayTimeCountsOnlyTodaysPartOfRunningSessions()
    {
        var s = NewGame();
        s.Candy.Sessions.Add(new PlaySession { Start = Local(11, 6, 20), End = Local(11, 7, 1) });  // 자정을 넘긴 구간: 오늘 몫 1시간
        s.Candy.Sessions.Add(new PlaySession { Start = Local(11, 7, 10), End = Local(11, 7, 11) }); // 1시간
        s.Daily.Day = StarlightLedger.DayKey(Local(11, 7, 12));
        Assert.Equal(2 * 3600, DailyGoals.PlayedTodaySeconds(s), 6);
    }

    [Fact]
    public void TravelTargetScalesWithSpeedAndLooksRound()
    {
        var s = NewGame();
        double slow = DailyGoals.Find("travel")!.Target(s);
        s.Upgrades[Upgrades.Engine] = 20;
        double fast = DailyGoals.Find("travel")!.Target(s);
        Assert.True(fast > slow * 3);
        Assert.True(slow >= GameRules.Speed(NewGame()) * 25 * 60);
        Assert.Equal(Math.Round(fast), fast);
    }
}
