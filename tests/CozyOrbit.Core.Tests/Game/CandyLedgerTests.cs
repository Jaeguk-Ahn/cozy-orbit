using CozyOrbit.Core.Game;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Tests.Game;

public class CandyLedgerTests
{
    private static readonly DateTimeOffset T0 = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Local));

    private static UsageEntry Out(string id, DateTimeOffset at, long output) =>
        UsageEntry.Create(id, at, "m", input: 0, output: output, cacheWrite: 0, cacheRead: 0);

    private static ProviderScan Scan(params UsageEntry[] entries) => new("p", "p", entries, null);

    [Fact]
    public void OnlyUsageWhileTheGameRunsBecomesCandy()
    {
        var c = new CandyState();
        CandyLedger.BeginSession(c, T0);
        var scan = new[] { Scan(Out("before", T0.AddMinutes(-5), 50_000), Out("during", T0.AddMinutes(5), 10_000)) };

        double gained = CandyLedger.Apply(c, scan, T0.AddMinutes(10));

        Assert.Equal(10_000 / CandyLedger.WeightedPerCandy, gained, 9);
        Assert.Equal(gained, c.Balance, 9);
    }

    [Fact]
    public void RescanningTheSameUsageGivesNothingMore()
    {
        var c = new CandyState();
        CandyLedger.BeginSession(c, T0);
        var scan = new[] { Scan(Out("a", T0.AddMinutes(1), 20_000)) };
        CandyLedger.Apply(c, scan, T0.AddMinutes(2));
        double again = CandyLedger.Apply(c, scan, T0.AddMinutes(4));

        Assert.Equal(0, again);
        Assert.Equal(20_000 / CandyLedger.WeightedPerCandy, c.Balance, 9);
    }

    [Fact]
    public void TimeTheGameWasClosedOrAsleepIsNotCounted()
    {
        var c = new CandyState();
        CandyLedger.BeginSession(c, T0);
        CandyLedger.Touch(c, T0.AddMinutes(30));
        // 30분 뒤 꺼졌다가 2시간 뒤 다시 켬
        CandyLedger.BeginSession(c, T0.AddHours(2));
        var scan = new[]
        {
            Scan(Out("first", T0.AddMinutes(10), 5_000), Out("closed", T0.AddHours(1), 900_000), Out("second", T0.AddHours(2).AddMinutes(1), 15_000)),
        };

        CandyLedger.Apply(c, scan, T0.AddHours(2).AddMinutes(2));

        Assert.Equal((5_000 + 15_000) / CandyLedger.WeightedPerCandy, c.Balance, 9);
    }

    [Fact]
    public void ReadFailureDoesNotTakeBackAndRecoveryCatchesUp()
    {
        var c = new CandyState();
        CandyLedger.BeginSession(c, T0);
        var a = Out("a", T0.AddMinutes(1), 30_000);
        var b = Out("b", T0.AddMinutes(2), 10_000);
        CandyLedger.Apply(c, new[] { Scan(a, b) }, T0.AddMinutes(3));
        double before = c.Balance;

        var dip = CandyLedger.Apply(c, new[] { new ProviderScan("p", "p", new[] { b }, "busy") }, T0.AddMinutes(4));
        Assert.Equal(0, dip);
        Assert.Equal(before, c.Balance, 9);

        CandyLedger.Apply(c, new[] { Scan(a, b, Out("c", T0.AddMinutes(4), 5_000)) }, T0.AddMinutes(5));
        Assert.Equal(45_000 / CandyLedger.WeightedPerCandy, c.Balance, 9);
    }

    [Fact]
    public void SessionsOlderThanAWeekAreDropped()
    {
        var c = new CandyState();
        CandyLedger.BeginSession(c, T0.AddDays(-10));
        CandyLedger.BeginSession(c, T0);
        Assert.Single(c.Sessions);
        Assert.Equal(T0, c.Sessions[0].Start);
    }
}
