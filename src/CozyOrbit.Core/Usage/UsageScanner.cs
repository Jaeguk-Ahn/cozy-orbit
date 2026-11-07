using CozyOrbit.Core.Infra;
using CozyOrbit.Core.Usage.Providers;

namespace CozyOrbit.Core.Usage;

/// <summary>한 프로바이더의 스캔 결과. Error 가 있으면 Entries 는 직전 성공 결과(since 이후만) — 없으면 빈 목록.</summary>
public sealed record ProviderScan(string ProviderId, string DisplayName, IReadOnlyList<UsageEntry> Entries, string? Error);

/// <summary>
/// 모든 프로바이더를 병렬로 읽는다 (원본 UsageStore.RefreshAsync 의 읽기 부분). 실패한 프로바이더는 직전 성공 결과를 돌려준다
/// (SQLite BUSY 등을 "사용량 0"으로 잘못 보이지 않게). 집계·저장은 하지 않는다 — 별빛 환산은 게임 쪽 몫.
/// </summary>
public sealed class UsageScanner
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly Dictionary<string, IReadOnlyList<UsageEntry>> _lastGood = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient? _http;

    /// <summary>프로바이더 하나의 읽기 시간 제한 (넘으면 직전 결과 유지 + 오류 표시)</summary>
    internal TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public UsageScanner(HttpClient? http = null, IReadOnlyList<IUsageProvider>? providers = null)
    {
        _http = http;
        _providers = providers ?? CreateDefaultProviders();
    }

    public IReadOnlyList<IUsageProvider> Providers => _providers;

    /// <summary>원본 등록 순서와 동일 (Cursor 제외 — OS 보안 저장소·대시보드 API 가 필요해 이 게임은 싣지 않는다)</summary>
    public static IReadOnlyList<IUsageProvider> CreateDefaultProviders() => new IUsageProvider[]
    {
        new ClaudeCodeUsageProvider(),
        new CodexUsageProvider(),
        new GeminiUsageProvider(),
        new AntigravityUsageProvider(),
        new OpenCodeUsageProvider(),
        new HermesUsageProvider(),
        new GrokUsageProvider(),
        new CopilotUsageProvider(),
        new KiroUsageProvider(),
        new PiUsageProvider(),
        new OmpUsageProvider(),
        new AsideUsageProvider(),
        new KimiCodeUsageProvider(),
    };

    /// <summary>
    /// since 이후 레코드를 모든 켜진 프로바이더에서 병렬로 읽는다. 프로바이더별 시간 제한 3분(넘으면 직전 결과 + 오류).
    /// 동시에 두 번 불리면 두 번째는 첫 번째가 끝날 때까지 기다린다.
    /// </summary>
    public async Task<IReadOnlyList<ProviderScan>> ScanAsync(DateTimeOffset since, IReadOnlySet<string> disabledProviders,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = UsageTime.Now;
            var enabled = _providers.Where(p => !disabledProviders.Contains(p.Id)).ToList();
            var tasks = enabled.Select(async p =>
            {
                // 시간 제한을 문맥 토큰으로도 넘긴다 — 기다리기만 끊으면 멈춘 프로바이더가 뒤에서 계속 돌고,
                // 다음 스캔이 같은 프로바이더를 또 띄운다
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ProviderTimeout);
                var ctx = new ProviderContext
                {
                    Now = now,
                    Since = since,
                    Http = _http,
                    CancellationToken = timeout.Token,
                };
                try
                {
                    var entries = await p.LoadAsync(ctx).WaitAsync(timeout.Token);
                    lock (_lastGood) _lastGood[p.Id] = entries;
                    return new ProviderScan(p.Id, p.DisplayName, After(entries, since), null);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    AppLog.Warn($"[{p.Id}] 사용량 읽기 실패: {ex.Message}");
                    IReadOnlyList<UsageEntry>? prev;
                    lock (_lastGood) _lastGood.TryGetValue(p.Id, out prev);
                    return new ProviderScan(p.Id, p.DisplayName, After(prev ?? Array.Empty<UsageEntry>(), since), ex.Message);
                }
            }).ToList();

            return await Task.WhenAll(tasks);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>파일 mtime 하한만으로는 since 이전 레코드가 섞일 수 있어 날짜로 한 번 더 거른다</summary>
    private static IReadOnlyList<UsageEntry> After(IReadOnlyList<UsageEntry> entries, DateTimeOffset since) =>
        entries.All(e => e.Date >= since) ? entries : entries.Where(e => e.Date >= since).ToList();
}
