using CozyOrbit.Core.Usage;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage;

public class PricingAndRootsTests
{
    [Fact]
    public void PricingUsesExactKeysAndAliases()
    {
        Assert.Equal("claude-sonnet-4-20250514", ModelPricing.ModelKey("Anthropic/claude-sonnet-4"));
        Assert.Null(ModelPricing.EstimatedCost("some-opus-like-model", 1000, 1000, 0, 0));
        // claude-opus-4-8: 5 / 25 / 6.25 / 0.5 per M
        var cost = ModelPricing.EstimatedCost("claude-opus-4-8", 1_000_000, 1_000_000, 0, 1_000_000);
        Assert.NotNull(cost);
        Assert.Equal(5 + 25 + 0.5, cost!.Value, 6);
    }

    [Fact]
    public void CacheWriteWithoutRateIsUnpriced() =>
        Assert.Null(ModelPricing.EstimatedCost("gpt-5", 10, 10, 10, 0));

    [Fact]
    public void LongContextMultiplier()
    {
        var c = ModelPricing.EstimatedCost("gpt-5.5", 300_000, 1_000_000, 0, 0)!.Value;
        Assert.Equal(300_000 * 5.0 / 1e6 * 2 + 30 * 1.5, c, 6);
    }

    [Fact]
    public void UnionDropsAncestorOfDefaults()
    {
        using var tmp = new TempDir();
        var def = Path.Combine(tmp.Path, ".claude", "projects");
        Directory.CreateDirectory(def);
        var extra = Path.Combine(tmp.Path, "extra-logs");
        Directory.CreateDirectory(extra);
        var roots = ScanRoots.Union(new[] { def }, $"{tmp.Path},{extra}");
        Assert.Equal(2, roots.Count);
        Assert.Contains(roots, r => r.EndsWith("projects"));
        Assert.Contains(roots, r => r.EndsWith("extra-logs"));
    }

    [Fact]
    public void CustomRootsExpandWildcards()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "acct-a", "projects"));
        Directory.CreateDirectory(Path.Combine(tmp.Path, "acct-b", "projects"));
        Directory.CreateDirectory(Path.Combine(tmp.Path, "other", "projects"));
        var roots = ScanRoots.ExpandCustom(Path.Combine(tmp.Path, "acct-*", "projects"));
        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void NormalizeFoldsDescendants()
    {
        using var tmp = new TempDir();
        var a = Path.Combine(tmp.Path, "a");
        var b = Path.Combine(a, "b");
        var roots = ScanRoots.Normalize(new[] { b, a, a });
        Assert.Single(roots);
        Assert.Equal(Path.GetFullPath(a), roots[0]);
    }

    [Fact]
    public void IsoParsingRequiresZone()
    {
        Assert.NotNull(UsageTime.ParseIso("2026-06-30T10:00:00.000Z"));
        Assert.NotNull(UsageTime.ParseIso("2026-06-30T10:00:00.1234567+09:00"));
        Assert.Null(UsageTime.ParseIso("2026-06-30T10:00:00"));
    }

    [Fact]
    public void MakeEntryAddsUnclassifiedToOutput()
    {
        var e = UsageEntry.Make("x", DateTimeOffset.UtcNow, "m", 100, 50, 20, 10, total: 250)!;
        Assert.Equal(120, e.Output);
        Assert.True(e.CostUnavailable);
        Assert.Null(UsageEntry.Make("z", DateTimeOffset.UtcNow, "m", 0, 0, 0, 0));
    }

    [Fact]
    public void DedupKeepsMaxTotalButEarliestDate()
    {
        var t0 = DateTimeOffset.Parse("2026-06-30T10:00:00Z");
        var a = UsageEntry.Create("id", t0, "m", 10, 1, 0, 0);
        var b = UsageEntry.Create("id", t0.AddMinutes(1), "m", 10, 50, 0, 0);
        var r = Dedup.KeepMax(new[] { a, b });
        Assert.Single(r);
        Assert.Equal(60, r[0].Total);
        Assert.Equal(t0, r[0].Date);
    }
}
