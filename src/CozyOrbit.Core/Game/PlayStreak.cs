using System.Globalization;

namespace CozyOrbit.Core.Game;

/// <summary>
/// 연속 출석: 게임이 켜져 있던 로컬 날짜가 하루씩 이어진 수 (도전과제용, GameStats 에 적는다).
/// 날짜는 오늘의 목표·별빛 장부와 같은 로컬 날짜(StarlightLedger.DayKey). 시각은 호출자가 넘긴다.
/// </summary>
public static class PlayStreak
{
    /// <summary>now 의 로컬 날짜에 켜져 있었다고 적는다. 기록이 바뀌었으면 true.
    /// 바로 다음 날이면 +1, 하루라도 건너뛰었으면 1 부터. 시계가 뒤로 간 날은 무시한다.</summary>
    public static bool Visit(GameState s, DateTimeOffset now)
    {
        var st = s.Stats;
        string today = StarlightLedger.DayKey(now);
        if (st.LastPlayDay == today) return false;
        if (st.LastPlayDay is { } last && TryDate(last, out var prev) && TryDate(today, out var cur))
        {
            if (cur <= prev) return false;
            st.PlayStreak = cur == prev.AddDays(1) ? st.PlayStreak + 1 : 1;
        }
        else
        {
            st.PlayStreak = 1;
        }
        st.BestPlayStreak = Math.Max(st.BestPlayStreak, st.PlayStreak);
        st.LastPlayDay = today;
        return true;
    }

    private static bool TryDate(string day, out DateTime date) =>
        DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
