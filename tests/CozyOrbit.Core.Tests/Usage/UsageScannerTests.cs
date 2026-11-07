using CozyOrbit.Core.Usage;

namespace CozyOrbit.Core.Tests.Usage;

public class UsageScannerTests
{
    private static readonly DateTimeOffset Since = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

    /// <summary>미리 정한 레코드를 주거나, FailFrom 번째 호출부터 예외를 던지는 프로바이더</summary>
    private sealed class FakeProvider(string id, IReadOnlyList<UsageEntry> entries, int failFrom = int.MaxValue) : IUsageProvider
    {
        public int Loads;
        public DateTimeOffset? SawSince;
        public string Id => id;
        public string DisplayName => id.ToUpperInvariant();
        public IReadOnlyList<string> ResolveRoots(ProviderContext ctx) => Array.Empty<string>();

        public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx)
        {
            SawSince = ctx.Since;
            if (++Loads >= failFrom) throw new InvalidOperationException("database is locked");
            return Task.FromResult(entries);
        }
    }

    private static UsageEntry Entry(string id, DateTimeOffset date, long input) => UsageEntry.Make(id, date, "m", input, 0, 0, 0)!;

    [Fact]
    public async Task FailureReturnsLastGoodEntriesWithError()
    {
        var fake = new FakeProvider("fake", new[] { Entry("a", Since.AddHours(1), 100) }, failFrom: 2);
        var scanner = new UsageScanner(providers: new IUsageProvider[] { fake });

        var first = (await scanner.ScanAsync(Since, new HashSet<string>())).Single();
        var second = (await scanner.ScanAsync(Since, new HashSet<string>())).Single();

        Assert.Null(first.Error);
        Assert.Equal("fake", second.ProviderId);
        Assert.Equal("FAKE", second.DisplayName);
        Assert.Equal("database is locked", second.Error);
        Assert.Equal(100, Assert.Single(second.Entries).Total);
    }

    [Fact]
    public async Task FailureWithoutLastGoodIsEmpty()
    {
        var fake = new FakeProvider("fake", Array.Empty<UsageEntry>(), failFrom: 1);
        var scan = (await new UsageScanner(providers: new IUsageProvider[] { fake }).ScanAsync(Since, new HashSet<string>())).Single();

        Assert.NotNull(scan.Error);
        Assert.Empty(scan.Entries);
    }

    [Fact]
    public async Task DisabledProviderIsNotInvoked()
    {
        var on = new FakeProvider("on", new[] { Entry("a", Since.AddHours(1), 1) });
        var off = new FakeProvider("off", new[] { Entry("b", Since.AddHours(1), 1) });
        var scanner = new UsageScanner(providers: new IUsageProvider[] { on, off });

        var scans = await scanner.ScanAsync(Since, new HashSet<string> { "off" });

        Assert.Equal("on", Assert.Single(scans).ProviderId);
        Assert.Equal(0, off.Loads);
        Assert.Equal(1, on.Loads);
    }

    [Fact]
    public async Task EntriesBeforeSinceAreExcluded()
    {
        var fake = new FakeProvider("fake", new[]
        {
            Entry("old", Since.AddSeconds(-1), 1),
            Entry("edge", Since, 10),
            Entry("new", Since.AddHours(3), 100),
        }, failFrom: 2);
        var scanner = new UsageScanner(providers: new IUsageProvider[] { fake });

        var ok = (await scanner.ScanAsync(Since, new HashSet<string>())).Single();
        Assert.Equal(Since, fake.SawSince);
        Assert.Equal(new[] { "edge", "new" }, ok.Entries.Select(e => e.Id).ToArray());

        // 직전 결과도 이번 since 로 다시 거른다
        var later = Since.AddHours(1);
        var failed = (await scanner.ScanAsync(later, new HashSet<string>())).Single();
        Assert.NotNull(failed.Error);
        Assert.Equal(new[] { "new" }, failed.Entries.Select(e => e.Id).ToArray());
    }

    /// <summary>문맥 토큰이 취소될 때까지 도는 프로바이더 (멈춘 로그 읽기 흉내)</summary>
    private sealed class HangingProvider : IUsageProvider
    {
        public readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Id => "hang";
        public string DisplayName => "Hang";
        public IReadOnlyList<string> ResolveRoots(ProviderContext ctx) => Array.Empty<string>();

        public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) => Task.Run(() => Spin(ctx.CancellationToken));

        private IReadOnlyList<UsageEntry> Spin(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(10);
                }
            }
            finally { Stopped.TrySetResult(); }
        }
    }

    [Fact]
    public async Task TimeoutCancelsTheProviderItself()
    {
        var hang = new HangingProvider();
        var scanner = new UsageScanner(providers: new IUsageProvider[] { hang }) { ProviderTimeout = TimeSpan.FromMilliseconds(200) };

        var scan = (await scanner.ScanAsync(Since, new HashSet<string>())).Single();

        Assert.NotNull(scan.Error);
        // 기다리기만 끊긴 게 아니라 프로바이더 안의 작업도 멈췄다
        var stopped = await Task.WhenAny(hang.Stopped.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(hang.Stopped.Task, stopped);
    }

    [Fact]
    public void DefaultProvidersHaveNoCursor()
    {
        var ids = UsageScanner.CreateDefaultProviders().Select(p => p.Id).ToList();
        Assert.Equal(13, ids.Count);
        Assert.Equal("claude_code", ids[0]);
        Assert.DoesNotContain("cursor", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
