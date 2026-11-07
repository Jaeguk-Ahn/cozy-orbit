using System.Globalization;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>화면용 숫자·시간 표기</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>소수 자리 고정 (끝의 0 은 지운다)</summary>
    public static string Number(double v, int decimals) =>
        Math.Round(v, decimals, MidpointRounding.AwayFromZero).ToString("0." + new string('#', Math.Max(1, decimals)), Inv);

    /// <summary>1234 → "1,234", 12345 → "12.3K", 1.2e6 → "1.2M" …</summary>
    public static string Compact(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
        double a = Math.Abs(v);
        if (a < 10_000) return Math.Floor(v).ToString("#,0", Inv);
        string[] units = { "K", "M", "B", "T", "Qa", "Qi" };
        int i = -1;
        while (a >= 1000 && i < units.Length - 1) { a /= 1000; v /= 1000; i++; }
        return (a >= 100 ? Math.Floor(v).ToString("0", Inv) : (Math.Floor(v * 10) / 10).ToString("0.#", Inv)) + units[i];
    }

    /// <summary>초 → "1시간 17분" / "1h 17m"</summary>
    public static string Duration(double seconds)
    {
        if (!(seconds > 0)) return L.T("0초", "0s");
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        if (t.TotalDays >= 1) return L.T($"{(int)t.TotalDays}일 {t.Hours}시간", $"{(int)t.TotalDays}d {t.Hours}h");
        if (t.TotalHours >= 1)
            return t.Minutes == 0 ? L.T($"{t.Hours}시간", $"{t.Hours}h") : L.T($"{t.Hours}시간 {t.Minutes}분", $"{t.Hours}h {t.Minutes}m");
        if (t.TotalMinutes >= 1)
            return t.Seconds == 0 ? L.T($"{t.Minutes}분", $"{t.Minutes}m") : L.T($"{t.Minutes}분 {t.Seconds}초", $"{t.Minutes}m {t.Seconds}s");
        return L.T($"{t.Seconds}초", $"{t.Seconds}s");
    }

    public static string Steps(double v) => L.T($"{Compact(v)} 별걸음", $"{Compact(v)} steps");
}
