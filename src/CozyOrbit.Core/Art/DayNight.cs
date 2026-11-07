namespace CozyOrbit.Core.Art;

/// <summary>
/// 장면의 밤 정도 (0 = 낮, 1 = 한밤). 밤은 22시 ~ 6시 — 기준 시각 앞뒤 30분씩 한 시간에 걸쳐 천천히 바뀐다.
/// 시각은 부르는 쪽이 넘긴다 (장면은 컴퓨터의 지역 시각을, 테스트는 정한 시각을).
/// </summary>
public static class DayNight
{
    /// <summary>밤이 반쯤 내려앉는 시각 (해 질 녘 · 새벽)</summary>
    public const double DuskHour = 22, DawnHour = 6;
    /// <summary>낮 ↔ 밤이 바뀌는 데 걸리는 시간 (기준 시각 앞뒤로 반씩)</summary>
    public const double FadeHours = 1;
    /// <summary>이만큼 어두우면 햄찌가 졸기 시작한다</summary>
    public const double SleepyAt = 0.5;

    public static double Night(TimeSpan timeOfDay)
    {
        double h = timeOfDay.TotalHours % 24;
        if (h < 0) h += 24;
        // 오후엔 해 질 녘 쪽, 오전엔 새벽 쪽만 본다
        return h >= 12 ? Ramp(h, DuskHour) : 1 - Ramp(h, DawnHour);
    }

    public static double Night(DateTime local) => Night(local.TimeOfDay);

    public static bool IsSleepy(double night) => night >= SleepyAt;

    /// <summary>center 앞 반 ~ 뒤 반 시간 동안 0 → 1</summary>
    private static double Ramp(double hour, double center) => Math.Clamp((hour - center) / FadeHours + 0.5, 0, 1);
}
