using Avalonia.Threading;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.App;

/// <summary>
/// 게임 상태의 주인. UI 스레드에서만 상태를 바꾼다 (사용량 스캔은 백그라운드에서 읽고 결과 적용만 UI 스레드).
/// - 진행: 100ms 마다 <see cref="GameRules.CatchUp"/> (절전·오프라인도 같은 경로)
/// - 저장: 30초마다 + 종료 시
/// - 사용량: 시작 직후 + 2분마다
/// </summary>
public sealed class GameHost : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);
    /// <summary>이보다 긴 공백 뒤의 진행은 "다녀왔어요" 요약으로 알린다</summary>
    private static readonly TimeSpan AwayThreshold = TimeSpan.FromMinutes(1);

    private readonly SaveStore _store;
    private readonly UsageScanner _scanner;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _save;
    private readonly DispatcherTimer _scan;
    private readonly CancellationTokenSource _cts = new();

    public GameState State { get; }
    public IReadOnlyList<IUsageProvider> Providers => _scanner.Providers;

    public bool Scanning { get; private set; }
    public DateTimeOffset? LastScanFinished { get; private set; }
    /// <summary>마지막 스캔의 오늘 소스별 사용량</summary>
    public IReadOnlyList<ProviderToday> Today { get; private set; } = Array.Empty<ProviderToday>();
    /// <summary>마지막으로 별빛이 들어온 지급</summary>
    public CreditResult? LastCredit { get; private set; }

    /// <summary>진행할 때마다 (UI 갱신용)</summary>
    public event Action? Ticked;
    public event Action<DiscoveryEvent>? Discovered;
    /// <summary>별빛·별사탕이 새로 들어왔을 때 (지급 결과, 받은 별사탕)</summary>
    public event Action<CreditResult, double>? Credited;
    /// <summary>스킨을 사거나 바꿨을 때</summary>
    public event Action? SkinsChanged;
    /// <summary>스캔이 끝났을 때 (별빛이 없어도)</summary>
    public event Action? Scanned;
    /// <summary>오래 비운 뒤 돌아왔을 때의 진행 요약</summary>
    public event Action<TickResult>? Returned;

    public GameHost(SaveStore? store = null, UsageScanner? scanner = null)
    {
        _store = store ?? new SaveStore();
        _scanner = scanner ?? new UsageScanner();
        State = _store.Load(DateTimeOffset.Now);
        L.SetLanguage(State.Settings.Language);

        _tick = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => Tick());
        _save = new DispatcherTimer(SaveInterval, DispatcherPriority.Background, (_, _) => Save());
        _scan = new DispatcherTimer(ScanInterval, DispatcherPriority.Background, (_, _) => _ = ScanNowAsync());
    }

    /// <summary>오프라인 진행을 따라잡고 타이머를 켠다. 첫 진행 요약은 Returned 로 알리므로 구독 후에 부른다.</summary>
    public void Start()
    {
        Tick();
        _tick.Start();
        _save.Start();
        _scan.Start();
        _ = ScanNowAsync();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        bool away = now - State.LastTick > AwayThreshold;
        // 별사탕은 켜 둔 동안의 사용량만: 실행 직후·절전에서 깨어난 직후엔 새 구간을 연다
        var session = State.Candy.Sessions.LastOrDefault();
        if (session == null || now - session.End > AwayThreshold) CandyLedger.BeginSession(State.Candy, now);
        else CandyLedger.Touch(State.Candy, now);
        var r = GameRules.CatchUp(State, now);
        if (away && r.Seconds > 0)
        {
            AppLog.Info($"자리 비움 진행 {r.Seconds:0}초, 거리 +{r.DistanceGained:0}, 발견 {r.Discoveries.Count}");
            Returned?.Invoke(r);
        }
        else
        {
            foreach (var d in r.Discoveries) Discovered?.Invoke(d);
        }
        Ticked?.Invoke();
    }

    public async Task ScanNowAsync()
    {
        if (Scanning) return;
        Scanning = true;
        Scanned?.Invoke();
        try
        {
            var now = DateTimeOffset.Now;
            var since = StarlightLedger.ScanSince(State.Ledger, now);
            var disabled = new HashSet<string>(State.Settings.DisabledProviders);
            var scans = await Task.Run(() => _scanner.ScanAsync(since, disabled, _cts.Token));
            // 스캔하는 동안 진행된 시간을 먼저 반영한 뒤 별빛을 담는다 (배터리 여유 계산이 지금 기준이 되게)
            GameRules.CatchUp(State, DateTimeOffset.Now);
            var credit = StarlightLedger.Apply(State, scans, DateTimeOffset.Now);
            double candy = CandyLedger.Apply(State.Candy, scans, DateTimeOffset.Now);
            Today = credit.Today;
            LastScanFinished = DateTimeOffset.Now;
            if (credit.TotalStarlight > 0 || candy > 0)
            {
                if (credit.TotalStarlight > 0) LastCredit = credit;
                AppLog.Info($"별빛 +{credit.StarlightAdded:0.#} (넘침 {credit.OverflowStarlight:0.#} → 별가루 {credit.OverflowDust:0.#}), 별사탕 +{candy:0.##}");
                Credited?.Invoke(credit, candy);
                Save();
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("사용량 스캔 실패", ex);
        }
        finally
        {
            Scanning = false;
            Scanned?.Invoke();
        }
    }

    public bool Buy(string upgradeId)
    {
        GameRules.CatchUp(State, DateTimeOffset.Now);
        if (!GameRules.TryBuy(State, upgradeId)) return false;
        Save();
        Ticked?.Invoke();
        return true;
    }

    public bool BuySkin(string skinId)
    {
        if (!Shop.TryBuy(State, skinId)) return false;
        AppLog.Info($"스킨 구매 {skinId}");
        Save();
        SkinsChanged?.Invoke();
        return true;
    }

    public bool EquipSkin(string skinId)
    {
        if (!Shop.Equip(State, skinId)) return false;
        Save();
        SkinsChanged?.Invoke();
        return true;
    }

    public void SetManualBoost(bool on)
    {
        GameRules.SetManualBoost(State, on);
        Ticked?.Invoke();
    }

    public void SetAutoBoost(bool on)
    {
        State.Settings.AutoBoost = on;
        Save();
        Ticked?.Invoke();
    }

    public void SetProviderEnabled(string providerId, bool enabled)
    {
        if (enabled) State.Settings.DisabledProviders.Remove(providerId);
        else State.Settings.DisabledProviders.Add(providerId);
        Save();
        _ = ScanNowAsync();
    }

    public void Save()
    {
        try { _store.Save(State); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("저장 실패", ex); }
    }

    public void Dispose()
    {
        _tick.Stop();
        _save.Stop();
        _scan.Stop();
        _cts.Cancel();
        GameRules.CatchUp(State, DateTimeOffset.Now);
        Save();
        _cts.Dispose();
    }
}
