using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class PlayStreakTests
{
    /// <summary>로컬 시각 (연속 출석은 로컬 날짜 기준)</summary>
    private static DateTimeOffset Local(int month, int day, int hour)
    {
        var t = new DateTime(2026, month, day, hour, 0, 0);
        return new DateTimeOffset(t, TimeZoneInfo.Local.GetUtcOffset(t));
    }

    private static GameState NewGame() => GameState.New(Local(11, 1, 8));

    [Fact]
    public void FirstVisitStartsAtOne()
    {
        var s = NewGame();
        Assert.True(PlayStreak.Visit(s, Local(11, 1, 9)));
        Assert.Equal(1, s.Stats.PlayStreak);
        Assert.Equal(1, s.Stats.BestPlayStreak);
        Assert.Equal(StarlightLedger.DayKey(Local(11, 1, 9)), s.Stats.LastPlayDay);

        // 같은 날 다시 와도 그대로
        Assert.False(PlayStreak.Visit(s, Local(11, 1, 23)));
        Assert.Equal(1, s.Stats.PlayStreak);
    }

    [Fact]
    public void ConsecutiveDaysIncrementAcrossMidnightAndMonths()
    {
        var s = NewGame();
        PlayStreak.Visit(s, Local(10, 30, 23));
        PlayStreak.Visit(s, Local(10, 31, 0));
        PlayStreak.Visit(s, Local(11, 1, 12));
        Assert.Equal(3, s.Stats.PlayStreak);
        Assert.Equal(3, s.Stats.BestPlayStreak);
    }

    [Fact]
    public void GapResetsButBestIsKept()
    {
        var s = NewGame();
        PlayStreak.Visit(s, Local(11, 1, 9));
        PlayStreak.Visit(s, Local(11, 2, 9));
        PlayStreak.Visit(s, Local(11, 3, 9));
        Assert.True(PlayStreak.Visit(s, Local(11, 5, 9)));   // 4일을 건너뜀
        Assert.Equal(1, s.Stats.PlayStreak);
        Assert.Equal(3, s.Stats.BestPlayStreak);

        PlayStreak.Visit(s, Local(11, 6, 9));
        Assert.Equal(2, s.Stats.PlayStreak);
        Assert.Equal(3, s.Stats.BestPlayStreak);
    }

    [Fact]
    public void ClockGoingBackDoesNothing()
    {
        var s = NewGame();
        PlayStreak.Visit(s, Local(11, 2, 9));
        PlayStreak.Visit(s, Local(11, 3, 9));
        Assert.False(PlayStreak.Visit(s, Local(11, 1, 9)));
        Assert.Equal(2, s.Stats.PlayStreak);
        Assert.Equal(StarlightLedger.DayKey(Local(11, 3, 9)), s.Stats.LastPlayDay);
    }

    [Fact]
    public void StreakAchievementReadsTheBestStreak()
    {
        var s = NewGame();
        var week = Achievements.Find("streak_7")!;
        for (int day = 1; day <= 6; day++) PlayStreak.Visit(s, Local(11, day, 9));
        Assert.False(Achievements.IsDone(s, week));
        PlayStreak.Visit(s, Local(11, 7, 9));
        Assert.True(Achievements.IsDone(s, week));
        PlayStreak.Visit(s, Local(11, 20, 9));   // 끊겨도 이미 이룬 것은 그대로
        Assert.True(Achievements.IsDone(s, week));
    }
}
