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
    /// <summary>이번 목적지 도감을 다 채워 새 항해를 떠날 수 있게 됐을 때 (한 번)</summary>
    public event Action? VoyageReady;
    /// <summary>새 항해를 떠났을 때 (새 목적지, 받은 별사탕)</summary>
    public event Action<DestinationDef, double>? VoyageStarted;
    /// <summary>범용 알림 (이미 현지화한 글, 아이콘 스프라이트 id) — 큰 창은 토스트, 플로팅은 선장 말풍선</summary>
    public event Action<string, string>? Noticed;

    /// <summary>플레이어에게 짧은 알림을 띄운다 (text 는 L.T 로 만든 글, sprite 는 SpriteLibrary id)</summary>
    public void Notify(string text, string sprite) => Noticed?.Invoke(text, sprite);

    private bool _voyageReady;
    private bool _rescanPending;

    public GameHost(SaveStore? store = null, UsageScanner? scanner = null)
    {
        _store = store ?? new SaveStore();
        _scanner = scanner ?? new UsageScanner();
        State = _store.Load(DateTimeOffset.Now);
        // 기록 그래프가 이 기능 이전 세이브에서도 비지 않게 별빛 장부로 채운다
        UsageHistory.SeedFromLedger(State);
        L.SetLanguage(State.Settings.Language);
        // 이미 항해해 온 옛 세이브엔 첫 실행 안내를 띄우지 않는다
        Onboarding.SkipIfAlreadySailing(State);
        Sectors.BackfillCompletedCount(State);

        _tick = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => Tick());
        _save = new DispatcherTimer(SaveInterval, DispatcherPriority.Background, (_, _) => Save());
        _scan = new DispatcherTimer(ScanInterval, DispatcherPriority.Background, (_, _) => _ = ScanNowAsync());
        // AI 도구 찾기: 지금 한 번 + 스캔이 끝날 때마다
        Scanned += () => { if (!Scanning) DetectProviders(); };
        DetectProviders();
        // 구역 완성 보상·연속 출석: 진행마다 (자리 비움 요약 경로의 발견도 놓치지 않게 Discovered 대신 Ticked)
        Ticked += CheckMilestones;
    }

    private int _milestoneVoyage = -1;
    private int _milestoneCount = -1;
    private DateTimeOffset _nextStreakCheck;

    /// <summary>발견 수가 바뀌었으면 구역 완성 보상을, 1분마다 연속 출석 날짜를 확인한다</summary>
    private void CheckMilestones()
    {
        var now = DateTimeOffset.Now;
        if (now >= _nextStreakCheck)
        {
            _nextStreakCheck = now.AddMinutes(1);
            PlayStreak.Visit(State, now);
        }
        if (State.Voyage == _milestoneVoyage && State.DiscoveryCount == _milestoneCount) return;
        _milestoneVoyage = State.Voyage;
        _milestoneCount = State.DiscoveryCount;

        var rewards = Sectors.ClaimCompleted(State);
        if (rewards.Count == 0) return;
        double dust = rewards.Sum(r => r.Stardust);
        double candy = rewards.Sum(r => r.Candy);
        AppLog.Info($"구역 완성 {string.Join(", ", rewards.Select(r => Sectors.Key(State.Voyage, r.Sector.Number)))}: 별가루 +{dust:0}, 별사탕 +{candy:0}");
        string text = rewards.Count == 1
            ? L.T($"{rewards[0].Sector.Name} 구역 완성!\n별가루 +{Fmt.Compact(dust)} · 별사탕 +{Fmt.Compact(candy)}",
                  $"{rewards[0].Sector.Name} complete!\nStardust +{Fmt.Compact(dust)} · Star candy +{Fmt.Compact(candy)}")
            : L.T($"구역 {rewards.Count}곳 완성!\n별가루 +{Fmt.Compact(dust)} · 별사탕 +{Fmt.Compact(candy)}",
                  $"{rewards.Count} sectors complete!\nStardust +{Fmt.Compact(dust)} · Star candy +{Fmt.Compact(candy)}");
        Infra.Sound.Play(CozyOrbit.Core.Audio.SoundId.Star);
        Save();
        Notify(text, "icon_goal");
    }

    /// <summary>우주 고래에게 인사했다 (도전과제용 기록)</summary>
    public void GreetWhale()
    {
        State.Stats.WhaleGreets++;
        Save();
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
        bool ready = Voyages.CanDepart(State);
        if (ready && !_voyageReady) VoyageReady?.Invoke();
        _voyageReady = ready;
        Ticked?.Invoke();
    }

    /// <param name="queueIfBusy">이미 도는 중이면 끝난 뒤 한 번 더 (설정을 바꿨을 때만 — 주기 스캔은 겹치면 그냥 건너뛴다)</param>
    public async Task ScanNowAsync(bool queueIfBusy = false)
    {
        if (Scanning)
        {
            // 도는 중인 스캔은 예전 설정(꺼 둔 소스 등)으로 읽는 중 — 끝나면 한 번 더
            if (queueIfBusy) _rescanPending = true;
            return;
        }
        try
        {
            Scanning = true;
            Scanned?.Invoke();
            var now = DateTimeOffset.Now;
            var since = StarlightLedger.ScanSince(State.Ledger, now);
            var disabled = new HashSet<string>(State.Settings.DisabledProviders);
            var scans = await Task.Run(() => _scanner.ScanAsync(since, disabled, _cts.Token));
            // 스캔하는 동안 진행된 시간을 먼저 반영한 뒤 별빛을 담는다 (배터리 여유 계산이 지금 기준이 되게).
            // Tick 으로 돌려야 절전에서 깬 직후에도 자리 비움 요약·별사탕 구간이 제대로 처리된다.
            Tick();
            var credit = StarlightLedger.Apply(State, scans, DateTimeOffset.Now);
            double candy = CandyLedger.Apply(State.Candy, scans, DateTimeOffset.Now);
            UsageHistory.Record(State, scans, candy, DateTimeOffset.Now);
            Today = credit.Today;
            LastScanFinished = DateTimeOffset.Now;
            if (credit.TotalStarlight > 0 || candy > 0)
            {
                if (credit.TotalStarlight > 0) LastCredit = credit;
                AppLog.Info($"별빛 +{credit.StarlightAdded:0.#} (넘침 {credit.OverflowStarlight:0.#} → 별가루 {credit.OverflowDust:0.#}), 별사탕 +{candy:0.##}");
                // 화면 쪽 처리에서 문제가 나도 받은 별빛은 남게 먼저 저장
                Save();
                Credited?.Invoke(credit, candy);
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
        if (_rescanPending && !_cts.IsCancellationRequested)
        {
            _rescanPending = false;
            await ScanNowAsync();
        }
    }

    /// <summary>틱 밖(구매·출발·별똥별)에서 진행을 따라잡는다 — 그 사이 발견도 알림·소리가 나게</summary>
    private void CatchUpNow()
    {
        var r = GameRules.CatchUp(State, DateTimeOffset.Now);
        foreach (var d in r.Discoveries) Discovered?.Invoke(d);
    }

    public bool Buy(string upgradeId)
    {
        CatchUpNow();
        if (!GameRules.TryBuy(State, upgradeId)) return false;
        Infra.Sound.Play(CozyOrbit.Core.Audio.SoundId.Buy);
        Save();
        Ticked?.Invoke();
        return true;
    }

    /// <summary>새 항해 떠나기 (도감을 다 채웠을 때만)</summary>
    public bool Depart()
    {
        CatchUpNow();
        double candy = Voyages.DepartureCandy(State);
        if (!Voyages.Depart(State)) return false;
        var dest = Destinations.Current(State);
        AppLog.Info($"새 항해 {State.Voyage}: {dest.Id}, 별사탕 +{candy}");
        _voyageReady = false;
        Save();
        VoyageStarted?.Invoke(dest, candy);
        Ticked?.Invoke();
        return true;
    }

    /// <summary>별똥별을 잡았다 (small = 유성우의 작은 별똥). 받은 별가루를 돌려준다.</summary>
    public double CatchShootingStar(bool small)
    {
        CatchUpNow();
        double reward = GameRules.CatchShootingStar(State, small);
        Ticked?.Invoke();
        return reward;
    }

    /// <summary>햄찌를 쓰다듬었다 (저장은 주기 저장에 맡긴다)</summary>
    public void PetHamster() => GameRules.Pet(State);

    public bool BuySkin(string skinId)
    {
        if (!Shop.TryBuy(State, skinId)) return false;
        AppLog.Info($"스킨 구매 {skinId}");
        Infra.Sound.Play(CozyOrbit.Core.Audio.SoundId.Buy);
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
        _ = ScanNowAsync(queueIfBusy: true);
    }

    // ── AI 도구 찾기 · 첫 실행 안내 ──

    /// <summary>프로바이더 id → 이 컴퓨터에서 기록 폴더를 찾았는지. 시작할 때와 스캔이 끝날 때마다 다시 본다 (찾기 전엔 비어 있음).</summary>
    public IReadOnlyDictionary<string, bool> ProviderFound { get; private set; } = new Dictionary<string, bool>();
    /// <summary>ProviderFound 가 바뀌었을 때 (UI 스레드)</summary>
    public event Action? ProvidersDetected;
    /// <summary>설정에서 "안내 다시 보기"를 눌렀을 때</summary>
    public event Action? OnboardingRequested;
    private bool _detecting;

    /// <summary>폴더가 있는지만 백그라운드에서 보고, 결과는 UI 스레드에서 바꾼다</summary>
    private void DetectProviders()
    {
        if (_detecting) return;
        _detecting = true;
        var providers = Providers;
        Task.Run(() =>
        {
            var now = DateTimeOffset.Now;
            var found = ProviderDetection.Detect(providers, new ProviderContext { Now = now, Since = now });
            Dispatcher.UIThread.Post(() =>
            {
                _detecting = false;
                var old = ProviderFound;
                bool changed = found.Count != old.Count || found.Any(kv => !old.TryGetValue(kv.Key, out var v) || v != kv.Value);
                ProviderFound = found;
                if (changed) ProvidersDetected?.Invoke();
            });
        });
    }

    /// <summary>첫 실행 안내를 다 봤거나 닫았다</summary>
    public void FinishOnboarding()
    {
        if (State.Settings.OnboardingDone) return;
        State.Settings.OnboardingDone = true;
        Save();
    }

    /// <summary>첫 실행 안내를 처음부터 다시 (설정)</summary>
    public void ReplayOnboarding()
    {
        State.Settings.OnboardingDone = false;
        Save();
        OnboardingRequested?.Invoke();
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
