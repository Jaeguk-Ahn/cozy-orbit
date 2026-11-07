using System.Globalization;

namespace CozyOrbit.Core.Usage;

/// <summary>날짜·기간 계산과 타임스탬프 파싱 공용 함수.</summary>
public static class UsageTime
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>테스트에서 "지금"을 고정하기 위한 훅</summary>
    public static Func<DateTimeOffset> NowProvider { get; set; } = () => DateTimeOffset.Now;
    public static DateTimeOffset Now => NowProvider();

    public static string LocalDay(DateTimeOffset date) => date.ToLocalTime().ToString("yyyy-MM-dd", Inv);
    public static string MonthKey(DateTimeOffset date) => date.ToLocalTime().ToString("yyyy-MM", Inv);
    public static string TodayKey() => LocalDay(Now);

    private static DateTimeOffset LocalMidnight(DateTime localDate) =>
        new(localDate.Date, TimeZoneInfo.Local.GetUtcOffset(localDate.Date));

    public static DateTimeOffset StartOfDay(DateTimeOffset now) => LocalMidnight(now.ToLocalTime().DateTime);

    /// <summary>주 시작 = 시스템 지역 설정의 첫 요일 (한국은 일요일)</summary>
    public static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var local = now.ToLocalTime().DateTime.Date;
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int diff = ((int)local.DayOfWeek - (int)first + 7) % 7;
        return LocalMidnight(local.AddDays(-diff));
    }

    public static DateTimeOffset StartOfMonth(DateTimeOffset now)
    {
        var local = now.ToLocalTime().DateTime;
        return LocalMidnight(new DateTime(local.Year, local.Month, 1));
    }

    /// <summary>블록·주·월을 한 번에 집계하기 위한 스캔 하한 (원본 enrichmentScanStart)</summary>
    public static DateTimeOffset EnrichmentScanStart(DateTimeOffset now)
    {
        var candidates = new[] { StartOfMonth(now), StartOfWeek(now), now.AddHours(-5), now.AddDays(-7) };
        return candidates.Min();
    }

    /// <summary>
    /// ISO-8601 파싱. 원본처럼 타임존(Z 또는 ±hh:mm)이 없는 문자열은 거부한다.
    /// 소수 초 길이는 자유.
    /// </summary>
    public static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        int t = s.IndexOf('T');
        if (t < 0) t = s.IndexOf(' ');
        if (t < 0) return null;
        var timePart = s[(t + 1)..];
        bool hasZone = timePart.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
                       || timePart.Contains('+') || timePart.LastIndexOf('-') > 0;
        if (!hasZone) return null;
        if (DateTimeOffset.TryParse(s, Inv, DateTimeStyles.AdjustToUniversal, out var d)) return d;
        return null;
    }

    /// <summary>epoch 초/밀리초 자동 판별 (원본 dateValue: 1e11 이상이면 ms)</summary>
    public static DateTimeOffset? FromEpochAuto(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0) return null;
        double seconds = raw >= 100_000_000_000 ? raw / 1000 : raw;
        return FromEpochSeconds(seconds);
    }

    public static DateTimeOffset? FromEpochSeconds(double seconds)
    {
        if (!double.IsFinite(seconds)) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static DateTimeOffset? FromEpochMs(double ms) =>
        double.IsFinite(ms) && ms > 0 ? FromEpochSeconds(ms / 1000) : null;
}
