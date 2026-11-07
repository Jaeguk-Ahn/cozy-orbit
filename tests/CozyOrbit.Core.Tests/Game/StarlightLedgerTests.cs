using CozyOrbit.Core.Game;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Tests.Game;

public class StarlightLedgerTests
{
    // 로컬 정오 — 날짜 경계(로컬 0시) 근처에서 "오늘"이 흔들리지 않게
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Local));

    private static GameState NewGame()
    {
        var s = GameState.New(Now);
        s.Settings.AutoBoost = false;
        return s;
    }

    private static UsageEntry Out(string id, DateTimeOffset at, long output) =>
        UsageEntry.Create(id, at, "m", input: 0, output: output, cacheWrite: 0, cacheRead: 0);

    private static ProviderScan Scan(string provider, params UsageEntry[] entries) => new(provider, provider, entries, null);

    [Fact]
    public void SameUsageIsCreditedOnlyOnce()
    {
        var s = NewGame();
        var scan = new[] { Scan("claude_code", Out("a", Now.AddHours(-1), 100_000)) };

        var first = StarlightLedger.Apply(s, scan, Now);
        var second = StarlightLedger.Apply(s, scan, Now.AddMinutes(2));

        Assert.Equal(Balance.DailyStarlight(100_000), first.StarlightAdded, 6);
        Assert.Equal(0, second.TotalStarlight);
        Assert.Equal(Balance.DailyStarlight(100_000), s.Starlight, 6);
    }

    [Fact]
    public void GrowthWithinADayFollowsTheCurveNotTheSum()
    {
        var s = NewGame();
        StarlightLedger.Apply(s, new[] { Scan("p", Out("a", Now.AddHours(-2), 100_000)) }, Now);
        StarlightLedger.Apply(s, new[] { Scan("p", Out("a", Now.AddHours(-2), 100_000), Out("b", Now.AddHours(-1), 100_000)) }, Now);

        // 두 번에 나눠 받아도 한 번에 200k 를 받은 것과 같다 (로그 곡선이라 2 × S(100k) 보다 적다)
        Assert.Equal(Balance.DailyStarlight(200_000), s.Starlight, 6);
        Assert.True(s.Starlight < 2 * Balance.DailyStarlight(100_000));
    }

    [Fact]
    public void FailedProviderDoesNotTakeBackAndRecoveryCatchesUpExactly()
    {
        var s = NewGame();
        var a = Out("a", Now.AddHours(-2), 300_000);
        var b = Out("b", Now.AddHours(-1), 200_000);
        StarlightLedger.Apply(s, new[] { Scan("p1", a), Scan("p2", b) }, Now);
        double afterFirst = s.Starlight;

        // p1 읽기 실패 (직전 결과도 없음) + p2 는 조금 늘어남 → 합계가 줄었으니 아무것도 주지 않는다
        var failed = new ProviderScan("p1", "p1", Array.Empty<UsageEntry>(), "busy");
        var dip = StarlightLedger.Apply(s, new[] { failed, Scan("p2", b, Out("c", Now, 50_000)) }, Now);
        Assert.Equal(0, dip.TotalStarlight);
        Assert.Equal(afterFirst, s.Starlight, 6);

        StarlightLedger.Apply(s, new[] { Scan("p1", a), Scan("p2", b, Out("c", Now, 50_000)) }, Now);
        Assert.Equal(Balance.DailyStarlight(550_000), s.Starlight, 6);
    }

    [Fact]
    public void UsageBeforeTheGameStartedIsIgnored()
    {
        var s = NewGame();
        var yesterday = s.Ledger.StartedAt.AddHours(-3);
        var r = StarlightLedger.Apply(s, new[] { Scan("p", Out("old", yesterday, 5_000_000)) }, Now);
        Assert.Equal(0, r.TotalStarlight);
    }

    [Fact]
    public void EachDayHasItsOwnCurve()
    {
        var s = NewGame();
        s.Ledger.StartedAt = Now.AddDays(-3);
        s.Upgrades[Upgrades.Battery] = 20; // 넘침 없이 담기게
        var r = StarlightLedger.Apply(s, new[]
        {
            Scan("p", Out("d1", Now.AddDays(-1), 100_000), Out("d0", Now.AddMinutes(-5), 100_000)),
        }, Now);
        Assert.Equal(2 * Balance.DailyStarlight(100_000), r.StarlightAdded, 6);
    }

    [Fact]
    public void FullBatteryTurnsTheRestIntoStardust()
    {
        var s = NewGame();
        double cap = GameRules.BatteryCapacity(s);
        s.Starlight = cap - 100;
        var r = StarlightLedger.Apply(s, new[] { Scan("p", Out("a", Now.AddMinutes(-1), 1_000_000)) }, Now);

        Assert.Equal(100, r.StarlightAdded, 6);
        Assert.Equal(cap, s.Starlight, 6);
        double overflow = Balance.DailyStarlight(1_000_000) - 100;
        Assert.Equal(overflow, r.OverflowStarlight, 6);
        Assert.Equal(overflow * Balance.OverflowDustPerStarlight, s.Stardust, 6);
    }

    [Fact]
    public void CacheReadsWeighFarLessThanOutput()
    {
        var e = UsageEntry.Create("x", Now, "m", input: 1000, output: 1000, cacheWrite: 1000, cacheRead: 1000);
        Assert.Equal(1000 + 250 + 250 + 10, StarlightLedger.Weighted(e), 6);
    }

    [Fact]
    public void ScanWindowStartsAtLastScanDayButNeverBeyondAWeek()
    {
        var ledger = new StarlightLedgerState { StartedAt = Now.AddDays(-30), LastScanAt = Now.AddDays(-2).AddHours(-1) };
        Assert.Equal(GameState.LocalMidnight(Now.AddDays(-2).AddHours(-1)), StarlightLedger.ScanSince(ledger, Now));

        ledger.LastScanAt = Now.AddDays(-20);
        Assert.Equal(Now - Balance.MaxScanBack, StarlightLedger.ScanSince(ledger, Now));

        var fresh = new StarlightLedgerState { StartedAt = GameState.LocalMidnight(Now) };
        Assert.Equal(GameState.LocalMidnight(Now), StarlightLedger.ScanSince(fresh, Now));
    }

    [Fact]
    public void OldLedgerDaysArePruned()
    {
        var s = NewGame();
        s.Ledger.CreditedWeighted["2000-01-01"] = 5;
        StarlightLedger.Apply(s, Array.Empty<ProviderScan>(), Now);
        Assert.DoesNotContain("2000-01-01", s.Ledger.CreditedWeighted.Keys);
    }
}
