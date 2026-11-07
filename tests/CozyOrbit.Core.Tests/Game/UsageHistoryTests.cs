using CozyOrbit.Core.Game;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Tests.Game;

public class UsageHistoryTests
{
    private static readonly DateTimeOffset T0 = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Local));

    private static UsageEntry Use(string id, DateTimeOffset at, long input, long output) =>
        UsageEntry.Create(id, at, "m", input: input, output: output, cacheWrite: 0, cacheRead: 0);

    private static ProviderScan Scan(string id, params UsageEntry[] entries) => new(id, id, entries, null);
    private static ProviderScan Failed(string id) => new(id, id, Array.Empty<UsageEntry>(), "busy");

    private static GameState NewState()
    {
        var s = GameState.New(T0);
        s.Ledger.StartedAt = GameState.LocalMidnight(T0.AddDays(-3));
        return s;
    }

    [Fact]
    public void RecordsTokensWeightedAndStarlightPerDay()
    {
        var s = NewState();
        var a = Use("a", T0.AddHours(-1), input: 1_000, output: 2_000);
        var b = Use("b", T0.AddDays(-1), input: 500, output: 0);
        var scans = new[] { Scan("p", a, b) };
        StarlightLedger.Apply(s, scans, T0);

        UsageHistory.Record(s, scans, 0, T0);

        var today = s.History[StarlightLedger.DayKey(T0)];
        Assert.Equal(3_000, today.Tokens);
        Assert.Equal(StarlightLedger.Weighted(a), today.Weighted, 9);
        Assert.Equal(Balance.DailyStarlight(StarlightLedger.Weighted(a)), today.Starlight, 9);
        Assert.Equal(500, s.History[StarlightLedger.DayKey(T0.AddDays(-1))].Tokens);
    }

    [Fact]
    public void UsageBeforeTheLedgerStartIsIgnored()
    {
        var s = NewState();
        var old = Use("old", T0.AddDays(-5), input: 0, output: 9_000);

        UsageHistory.Record(s, new[] { Scan("p", old) }, 0, T0);

        Assert.Empty(s.History);
    }

    [Fact]
    public void FailedProviderScanDoesNotLowerTheRecord()
    {
        var s = NewState();
        var day = StarlightLedger.DayKey(T0);
        var first = new[] { Scan("p", Use("a", T0.AddHours(-1), 0, 4_000)), Scan("q", Use("b", T0.AddHours(-2), 0, 6_000)) };
        StarlightLedger.Apply(s, first, T0);
        UsageHistory.Record(s, first, 0, T0);
        var before = s.History[day];
        long tokens = before.Tokens;
        double weighted = before.Weighted, starlight = before.Starlight;

        // q 를 읽지 못함 (직전 결과도 없음) → 합계가 줄어 보이지만 기록은 그대로
        var second = new[] { Scan("p", Use("a", T0.AddHours(-1), 0, 4_000)), Failed("q") };
        StarlightLedger.Apply(s, second, T0.AddMinutes(2));
        UsageHistory.Record(s, second, 0, T0.AddMinutes(2));

        var after = s.History[day];
        Assert.Equal(10_000, tokens);
        Assert.Equal(tokens, after.Tokens);
        Assert.Equal(weighted, after.Weighted, 9);
        Assert.Equal(starlight, after.Starlight, 9);

        // 복구 + 새 사용량 → 늘어난 만큼 따라간다
        var third = new[] { Scan("p", Use("a", T0.AddHours(-1), 0, 4_000), Use("c", T0.AddMinutes(3), 0, 1_000)), Scan("q", Use("b", T0.AddHours(-2), 0, 6_000)) };
        StarlightLedger.Apply(s, third, T0.AddMinutes(4));
        UsageHistory.Record(s, third, 0, T0.AddMinutes(4));
        Assert.Equal(11_000, s.History[day].Tokens);
        Assert.True(s.History[day].Starlight > starlight);
    }

    [Fact]
    public void CandyAccumulatesOnToday()
    {
        var s = NewState();
        var day = StarlightLedger.DayKey(T0);

        UsageHistory.Record(s, Array.Empty<ProviderScan>(), 1.5, T0);
        UsageHistory.Record(s, Array.Empty<ProviderScan>(), 0, T0.AddMinutes(2));
        UsageHistory.Record(s, Array.Empty<ProviderScan>(), 2.25, T0.AddMinutes(4));

        Assert.Equal(3.75, s.History[day].Candy, 9);
        Assert.Single(s.History);
    }

    [Fact]
    public void OldDaysArePruned()
    {
        var s = NewState();
        var old = StarlightLedger.DayKey(T0.AddDays(-(UsageHistory.KeepDays + 1)));
        var kept = StarlightLedger.DayKey(T0.AddDays(-(UsageHistory.KeepDays - 1)));
        s.History[old] = new DayUsage { Tokens = 1 };
        s.History[kept] = new DayUsage { Tokens = 2 };

        UsageHistory.Record(s, Array.Empty<ProviderScan>(), 0, T0);

        Assert.False(s.History.ContainsKey(old));
        Assert.True(s.History.ContainsKey(kept));
    }

    [Fact]
    public void SeedFillsMissingDaysFromTheLedgerOnly()
    {
        var s = NewState();
        s.Ledger.CreditedWeighted["2026-11-05"] = 50_000;
        s.Ledger.CreditedWeighted["2026-11-06"] = 80_000;
        s.History["2026-11-06"] = new DayUsage { Tokens = 123, Weighted = 90_000, Starlight = 7 };

        UsageHistory.SeedFromLedger(s);

        Assert.Equal(50_000, s.History["2026-11-05"].Weighted, 9);
        Assert.Equal(Balance.DailyStarlight(50_000), s.History["2026-11-05"].Starlight, 9);
        Assert.Equal(0, s.History["2026-11-05"].Tokens);
        Assert.Equal(123, s.History["2026-11-06"].Tokens);
        Assert.Equal(90_000, s.History["2026-11-06"].Weighted, 9);
    }
}
