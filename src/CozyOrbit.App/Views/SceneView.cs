using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CozyOrbit.App.Rendering;
using CozyOrbit.App.Views.Scene;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views;

/// <summary>
/// 우주 장면: 테마 하늘·장식 + 별 3겹 시차 스크롤 + 탐사선(스킨) + 다가오는/지나가는 천체 + 부스트 반짝이
/// + 우주 이벤트(별똥별·유성우·고래). 아래에는 연구소(소품 + 돌아다니는 햄찌 크루).
/// 햄찌를 누르면 쓰다듬기 반응, 별똥별을 누르면 별가루. ShowEventBubbles 면 발견·지급 알림을 말풍선으로 (플로팅 창).
/// 밤(지역 시각 22~6시, DayNight)엔 연구소가 푸르게 어둑해지고 등불이 번지며, 햄찌는 덜 걷고 가끔 존다 (누르면 깬다).
/// 별빛이 들어오면(Credited) 위에서 탐사선으로 반짝이가 쏟아진다. 말풍선은 탐사선을 가리지 않게 비켜선다.
/// 배율은 높이로 정한다 (플로팅 창처럼 작으면 2, 큰 창이면 3). 좌표는 "도트 단위" × 정수 배율.
/// </summary>
public sealed class SceneView : Control
{
    public GameHost? Host { get; set; }

    /// <summary>발견·별사탕 같은 알림을 선장 말풍선으로 보여 줄지 (큰 창은 토스트가 있으므로 끈다)</summary>
    public bool ShowEventBubbles { get; set; }

    /// <summary>탐사선이 그려지는 칸 (px, 출렁임 가운데 기준). 첫 그리기 전엔 비어 있다 — 큰 창 토스트가 피해 갈 때 쓴다.</summary>
    public Rect ShipBounds { get; private set; }

    /// <summary>밤낮을 정하는 지역 시각 (헤드리스 확인 도구가 바꿔 끼운다)</summary>
    internal Func<DateTime> LocalClock { get; set; } = () => DateTime.Now;

    private const int LabHeightUnits = 22;
    private const double FrameInterval = 1.0 / 30;
    /// <summary>햄찌 걷는 속도 (도트 단위/초)</summary>
    private const double WalkSpeed = 7;
    /// <summary>걸음 프레임 하나의 길이</summary>
    private const double StepSeconds = 0.16;
    /// <summary>별빛이 들어올 때 반짝이 소나기 길이 (초)</summary>
    private const double CreditShowerSeconds = 1.5;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new();
    private readonly Star[] _stars;
    private readonly List<Particle> _particles = new();
    private readonly List<Floater> _floaters = new();
    private readonly List<Walker> _walkers = new();
    private readonly List<Rect> _hamsterRects = new();
    private readonly SceneBubbles _bubbles = new();
    private readonly SceneEvents _events;
    private double _lastT;
    private double _scroll;
    private double _visualSpeed = 8;
    private double _sparkleDue;
    private int _captainIndex;
    /// <summary>밤 정도 0..1 (DayNight) — 진행할 때마다 지역 시각으로 다시 잰다</summary>
    private double _night;
    // 별빛 도착: 위에서 탐사선으로 빨려 드는 반짝이
    private readonly List<Mote> _motes = new();
    private double _creditedAt = double.NegativeInfinity;
    private double _moteDue;

    // 지나가는 천체 (발견 직후 왼쪽으로 빠져나간다)
    private int _seenDiscoveries = -1;
    private int _seenVoyage = -1;
    private string? _passingSprite;
    private double _passingX;
    private double _passingY;
    private double _lastUpcomingX = double.NaN;

    private readonly record struct Star(double X, double Y, int Layer, double Phase);
    private sealed class Particle { public double X, Y, Age; }
    /// <summary>탐사선으로 날아드는 별빛 한 알 (출발점 → 탐사선 가운데)</summary>
    private sealed class Mote { public double X0, Y0, Age, Life; }
    /// <summary>위로 둥실 떠오르는 작은 그림 (하트·음표·반짝이·Z)</summary>
    private sealed class Floater { public required string Sprite; public double X, Y, Age, Life = 1.2, Drift, Rise = 14; }

    /// <summary>돌아다니는 햄찌 하나. X·Target 은 제자리(칸 가운데)로부터의 거리 (도트 단위).</summary>
    private sealed class Walker
    {
        public double X, Target, Wait, StepClock, Phase;
        /// <summary>쓰다듬은 시각 (높이 폴짝)</summary>
        public double PettedAt = double.NegativeInfinity;
        /// <summary>남은 조는 시간 (밤에만, 초). 쓰다듬으면 깬다.</summary>
        public double DozeLeft;
        /// <summary>다음 Z 가 떠오를 때까지</summary>
        public double ZzzDue;
        public bool Left;
        public bool Dozing => DozeLeft > 0;
        public bool Walking => !Dozing && Wait <= 0 && Math.Abs(Target - X) > 1e-6;
    }

    private static readonly (string Ko, string En)[] PetLines =
    [
        ("찍!", "Squeak!"), ("간질간질~", "Tickles~"), ("도토리 최고!", "Acorns rule!"), ("오늘도 같이 힘내요!", "Let's do our best today!"),
        ("해바라기씨 주세요", "Sunflower seeds, please"), ("우주 최고 연구소!", "Best lab in space!"), ("헤헤", "Hehe"),
        ("별이 반짝반짝", "Twinkle twinkle"), ("일 열심히 하고 있어요?", "Working hard?"), ("쉬엄쉬엄 해요~", "Take it easy~"),
    ];

    private static readonly (string Ko, string En)[] WakeLines =
    [
        ("하암… 깼어요!", "Yawn… I'm up!"), ("쿨쿨… 앗, 안녕하세요!", "Zzz… oh, hi!"), ("꿈에서 해바라기씨 먹었어요", "I dreamt of sunflower seeds"),
        ("조금만 더 잘래요…", "Five more minutes…"), ("별 꿈 꿨어요", "I dreamt of stars"),
    ];

    public SceneView()
    {
        ClipToBounds = true;
        _stars = Enumerable.Range(0, 70).Select(i => new Star(_rng.NextDouble(), _rng.NextDouble(), i < 44 ? 0 : i < 64 ? 1 : 2, _rng.NextDouble() * 10)).ToArray();
        _events = new SceneEvents(_rng, 0);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(FrameInterval), DispatcherPriority.Render, (_, _) => Advance());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastT = _clock.Elapsed.TotalSeconds;
        _timer.Start();
        if (Host is { } host)
        {
            host.Discovered += OnDiscovered;
            host.Credited += OnCredited;
            host.Returned += OnReturned;
            host.Noticed += OnNoticed;
            host.VoyageReady += OnVoyageReady;
            host.VoyageStarted += OnVoyageStarted;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        if (Host is { } host)
        {
            host.Discovered -= OnDiscovered;
            host.Credited -= OnCredited;
            host.Returned -= OnReturned;
            host.Noticed -= OnNoticed;
            host.VoyageReady -= OnVoyageReady;
            host.VoyageStarted -= OnVoyageStarted;
        }
        base.OnDetachedFromVisualTree(e);
    }

    // ── 알림 말풍선 (플로팅 창) ──

    private void Notify(string text, string? icon)
    {
        if (ShowEventBubbles) _bubbles.Enqueue(new SceneBubbles.Bubble { Text = text, Icon = icon, Hamster = -1 });
    }

    private void OnDiscovered(DiscoveryEvent d)
    {
        Notify(L.T($"{d.Name} 발견!", $"Found {d.Name}!"), d.Sprite);
        if (d.Id != null && Decor.SouvenirFor(d.Id) is { } souvenir)
            Notify(L.T($"기념품: {souvenir.Name}", $"Souvenir: {souvenir.Name}"), souvenir.Sprite);
    }

    private void OnCredited(CreditResult c, double candy)
    {
        // 큰 창·플로팅 모두: 위에서 탐사선으로 별빛이 쏟아져 들어온다
        if (c.TotalStarlight > 0 || candy > 0) StartCreditShower();
        if (candy >= 0.1) Notify(L.T($"별사탕 +{Fmt.Number(candy, 1)}", $"Star candy +{Fmt.Number(candy, 1)}"), "icon_candy");
        else if (c.StarlightAdded > 0) Notify(L.T($"별빛 +{Fmt.Duration(c.StarlightAdded)}", $"Starlight +{Fmt.Duration(c.StarlightAdded)}"), "icon_starlight");
    }

    private void StartCreditShower()
    {
        _creditedAt = _clock.Elapsed.TotalSeconds;
        _moteDue = 0;
    }

    private void OnReturned(TickResult r)
    {
        Notify(L.T($"다녀오셨어요! {Fmt.Steps(r.DistanceGained)} 왔어요", $"Welcome back! We flew {Fmt.Steps(r.DistanceGained)}"),
            r.Discoveries.Count > 0 ? r.Discoveries[^1].Sprite : null);
        // 자리 비움 상한에 걸렸으면: 실제로 난 시간 + (더 늘릴 수 있으면) 침낭 연구 귀띔
        if (r.CappedSeconds >= 60 && Host is { } host)
        {
            var bunk = Upgrades.Get(Upgrades.Bunk);
            bool more = !bunk.IsMaxed(host.State.Level(Upgrades.Bunk));
            Notify(more
                    ? L.T($"{Fmt.Duration(r.Seconds)}만 날았어요 · 포근한 침낭으로 더 길게", $"Flew only {Fmt.Duration(r.Seconds)} · a Cozy Sleeping Bag helps")
                    : L.T($"{Fmt.Duration(r.Seconds)}만 날았어요", $"Flew only {Fmt.Duration(r.Seconds)}"),
                "icon_bunk");
        }
    }

    private void OnVoyageReady() => Notify(L.T("도감 완성! 새 항해를 떠날 수 있어요", "Atlas complete! A new voyage awaits"), "icon_voyage");

    private void OnVoyageStarted(DestinationDef d, double candy) => Notify(L.T($"{d.Name}(으)로 출발!", $"Off to {d.Name}!"), d.Sprite);

    private void OnNoticed(string text, string sprite) => Notify(text, sprite);

    // ── 진행 ──

    private int Scale => Math.Max(2, (int)Math.Floor(Bounds.Height / 86));

    private void Advance()
    {
        if (!IsEffectivelyVisible) return;
        double t = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(t - _lastT, 0, 0.25);
        _lastT = t;
        _night = DayNight.Night(LocalClock());

        var s = Host?.State;
        bool boosting = s != null && GameRules.IsBoosting(s);
        double speed = s != null ? GameRules.CurrentSpeed(s) : 1;
        // 실제 속도는 수십~수만 배까지 커지므로 화면 속도는 로그로 눌러 담는다 (도트 단위/초)
        double target = Math.Clamp(8 * (1 + 0.45 * Math.Log2(Math.Max(1, speed))), 8, 70);
        _visualSpeed += (target - _visualSpeed) * Math.Min(1, dt * 2.5);
        _scroll += _visualSpeed * dt;

        int scale = Scale;
        double probeX = ProbeX(scale);
        double probeY = SpaceHeight(scale) / 2;

        // 부스트 반짝이
        if (boosting)
        {
            _sparkleDue -= dt;
            while (_sparkleDue <= 0)
            {
                _sparkleDue += 0.07;
                _particles.Add(new Particle { X = probeX + 2 * scale, Y = probeY + (_rng.NextDouble() * 10 - 5) * scale, Age = 0 });
            }
        }
        foreach (var p in _particles)
        {
            p.Age += dt;
            p.X -= _visualSpeed * 1.3 * scale * dt;
        }
        _particles.RemoveAll(p => p.Age > 0.7);

        foreach (var f in _floaters)
        {
            f.Age += dt;
            f.Y -= f.Rise * scale * dt;
            f.X += f.Drift * scale * dt;
        }
        _floaters.RemoveAll(f => f.Age >= f.Life);

        // 별빛 도착: 앞쪽 동안 위에서 반짝이를 흩뿌리고, 하나하나 탐사선으로 빨려 든다
        if (t - _creditedAt < CreditShowerSeconds - 0.4)
        {
            var ship = ShipCenter(scale);
            _moteDue -= dt;
            while (_moteDue <= 0)
            {
                _moteDue += 0.045;
                _motes.Add(new Mote
                {
                    X0 = ship.X + (_rng.NextDouble() * 2 - 1) * 24 * scale,
                    Y0 = _rng.NextDouble() * Math.Max(scale, ship.Y - 14 * scale),
                    Life = 0.45 + _rng.NextDouble() * 0.3,
                });
            }
        }
        foreach (var m in _motes) m.Age += dt;
        _motes.RemoveAll(m => m.Age >= m.Life);

        // 발견이 일어나면 다가오던 천체를 "지나가는" 천체로 넘긴다 (새 항해로 번호가 처음부터면 넘기지 않는다)
        if (s != null)
        {
            if (_seenVoyage == s.Voyage && _seenDiscoveries >= 0 && s.DiscoveryCount > _seenDiscoveries)
            {
                _passingSprite = Destinations.SpriteOf(Destinations.Current(s), s.DiscoveryCount - 1);
                _passingX = double.IsNaN(_lastUpcomingX) ? probeX : _lastUpcomingX;
                _passingY = ObjectY(s.DiscoveryCount - 1, scale);
            }
            _seenDiscoveries = s.DiscoveryCount;
            _seenVoyage = s.Voyage;
        }
        if (_passingSprite != null)
        {
            _passingX -= _visualSpeed * 1.6 * scale * dt;
            if (_passingX < -40 * scale) _passingSprite = null;
        }

        _events.Update(t, dt, Bounds.Width, SpaceHeight(scale), scale);
        _bubbles.Update(dt);
        WalkCrew(s, scale, dt, t);

        InvalidateVisual();
    }

    private double SpaceHeight(int scale) => Bounds.Height - LabHeightUnits * scale;
    private double ProbeX(int scale) => Math.Round(Bounds.Width * 0.22);
    /// <summary>탐사선 가운데 (그리기 전이면 대략의 자리)</summary>
    private Point ShipCenter(int scale) =>
        ShipBounds.Width > 0 ? ShipBounds.Center : new Point(ProbeX(scale) + 8 * scale, SpaceHeight(scale) / 2);
    private double ObjectY(int index, int scale) =>
        Math.Round(SpaceHeight(scale) / 2 + (index % 2 == 0 ? -1 : 1) * 14 * scale);

    /// <summary>연구소 칸 나누기: 햄찌 수, 칸 간격(px), 첫 칸 왼쪽(px), 선장 칸 번호</summary>
    private (int Count, double Spacing, double StartX, int Captain) LabSlots(GameState? s, int scale)
    {
        int count = 1 + (s != null ? GameRules.CrewCount(s) : 0);
        double w = Bounds.Width;
        double spacing = Math.Min(34 * scale, (w - 8 * scale) / count);
        double startX = (w - spacing * (count - 1) - 16 * scale) / 2;
        return (count, spacing, startX, count / 2);
    }

    /// <summary>햄찌마다 잠깐 쉬었다가 제자리 근처의 새 지점으로 걸어간다. 밤엔 덜 걷고 가끔 존다.</summary>
    private void WalkCrew(GameState? s, int scale, double dt, double t)
    {
        var (count, spacing, _, _) = LabSlots(s, scale);
        bool sleepy = DayNight.IsSleepy(_night);
        while (_walkers.Count < count)
        {
            var walker = new Walker { Wait = 0.5 + _rng.NextDouble() * 3, Phase = _rng.NextDouble() * 10, Left = _rng.Next(2) == 0 };
            // 밤에 켜면 몇몇은 벌써 꾸벅꾸벅
            if (sleepy && _rng.Next(2) == 0) walker.DozeLeft = 6 + _rng.NextDouble() * 14;
            _walkers.Add(walker);
        }
        if (_walkers.Count > count) _walkers.RemoveRange(count, _walkers.Count - count);

        // 혼자면 바닥 가운데를 넓게, 여럿이면 제 칸 안에서만 (서로 겹치지 않게)
        double range = count == 1 ? Math.Min(48, Bounds.Width / scale / 2 - 14) : Math.Min(26, spacing / scale / 2 - 9);
        range = Math.Max(0, range);
        for (int i = 0; i < _walkers.Count; i++)
        {
            var w = _walkers[i];
            if (w.Dozing)
            {
                w.DozeLeft -= dt;
                w.ZzzDue -= dt;
                if (w.ZzzDue <= 0 && i < _hamsterRects.Count)
                {
                    w.ZzzDue = 1.3 + _rng.NextDouble() * 0.6;
                    var r = _hamsterRects[i];
                    double side = w.Left ? r.X + 2 * scale : r.Right - 7 * scale;
                    _floaters.Add(new Floater { Sprite = "fx_zzz", X = side, Y = r.Y - scale, Rise = 5, Drift = w.Left ? -2 : 2, Life = 2.2 });
                }
                if (!w.Dozing) w.Wait = 1 + _rng.NextDouble() * 2;
                continue;
            }
            if (w.Wait > 0)
            {
                w.Wait -= dt;
                if (w.Wait <= 0)
                {
                    if (sleepy && _rng.Next(2) == 0)
                    {
                        // 걷는 대신 그 자리에서 꾸벅
                        w.DozeLeft = 10 + _rng.NextDouble() * 16;
                        w.ZzzDue = 0.6;
                        continue;
                    }
                    w.Target = Math.Round((_rng.NextDouble() * 2 - 1) * range);
                }
                continue;
            }
            double gap = w.Target - w.X;
            double step = WalkSpeed * (sleepy ? 0.6 : 1) * dt;
            if (Math.Abs(gap) <= step)
            {
                w.X = w.Target;
                w.StepClock = 0;
                w.Wait = (1.5 + _rng.NextDouble() * 4) * (sleepy ? 2 : 1);
            }
            else
            {
                w.X += Math.Sign(gap) * step;
                w.Left = gap < 0;
                w.StepClock += dt;
            }
            w.X = Math.Clamp(w.X, -range, range);
        }
    }

    // ── 누르기: 햄찌 쓰다듬기 · 별똥별 잡기 · 고래 인사 ──

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Host is not { } host) return;
        var p = e.GetPosition(this);
        int scale = Scale;
        double t = _clock.Elapsed.TotalSeconds;

        if (_events.HitStar(p, scale) is { } star)
        {
            _events.Remove(star);
            double reward = host.CatchShootingStar(star.Small);
            Infra.Sound.Play(Core.Audio.SoundId.Star);
            for (int i = 0; i < 4; i++)
                _floaters.Add(new Floater { Sprite = "sparkle", X = star.X + (_rng.NextDouble() * 8 - 4) * scale, Y = star.Y, Drift = _rng.NextDouble() * 10 - 5, Life = 0.8 });
            _bubbles.Interrupt(new SceneBubbles.Bubble { Text = L.T($"별가루 +{Fmt.Compact(reward)}!", $"Stardust +{Fmt.Compact(reward)}!"), Icon = "icon_stardust", At = new Point(star.X, star.Y), Duration = 1.8 });
            e.Handled = true;
            return;
        }
        if (_events.HitWhale(p, scale))
        {
            // 기록은 지나가는 고래 한 마리에 한 번만 (인사 반응·소리는 매번)
            if (_events.CurrentWhale is { Greeted: false } whale)
            {
                whale.Greeted = true;
                host.GreetWhale();
            }
            Infra.Sound.Play(Core.Audio.SoundId.Whale);
            _bubbles.Interrupt(new SceneBubbles.Bubble { Text = L.T("뿌우~ 안녕!", "Fwoo~ hello!"), At = p, Duration = 2 });
            _floaters.Add(new Floater { Sprite = "fx_note", X = p.X, Y = p.Y, Drift = 3 });
            e.Handled = true;
            return;
        }
        for (int i = 0; i < _hamsterRects.Count && i < _walkers.Count; i++)
        {
            if (!_hamsterRects[i].Inflate(2 * scale).Contains(p)) continue;
            var w = _walkers[i];
            // 졸던 햄찌는 깨어나며 잠꼬대 한마디
            bool woke = w.Dozing;
            w.DozeLeft = 0;
            w.Target = w.X;
            // 깨운 햄찌는 잠깐은 깨어 있다 (바로 다시 꾸벅하지 않게)
            w.Wait = Math.Max(w.Wait, woke ? 6 : 1.4);
            w.PettedAt = t;
            var r = _hamsterRects[i];
            _floaters.Add(new Floater { Sprite = "fx_heart", X = r.X + r.Width / 2 - 3 * scale, Y = r.Y, Drift = _rng.NextDouble() * 6 - 3 });
            if (_rng.Next(3) == 0) _floaters.Add(new Floater { Sprite = "fx_note", X = r.X + r.Width / 2 + 2 * scale, Y = r.Y + 2 * scale, Drift = 4, Life = 1.4 });
            var line = woke ? WakeLines[_rng.Next(WakeLines.Length)] : PetLines[_rng.Next(PetLines.Length)];
            _bubbles.Interrupt(new SceneBubbles.Bubble { Text = L.T(line.Ko, line.En), Hamster = i, Duration = 2.2 });
            host.PetHamster();
            Infra.Sound.Play(Core.Audio.SoundId.Squeak);
            e.Handled = true;
            return;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        int scale = Scale;
        bool clickable = _events.HitStar(p, scale) != null || _events.HitWhale(p, scale) || _hamsterRects.Any(r => r.Inflate(2 * scale).Contains(p));
        Cursor = clickable ? new Cursor(StandardCursorType.Hand) : null;
    }

    // ── 그리기 ──

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        int scale = Scale;
        double t = _clock.Elapsed.TotalSeconds;
        double spaceH = SpaceHeight(scale);
        var s = Host?.State;
        var theme = s != null ? SkinArt.Theme(s) : SceneThemes.Space;

        dc.FillRectangle(SkinArt.Sky(theme), new Rect(0, 0, w, spaceH));

        // 테마 장식 (가장 먼 배경)
        foreach (var d in theme.Decorations)
        {
            if (!SpriteLibrary.TryGet(d.Sprite, out var deco)) continue;
            double dw = deco.Width * scale;
            double x = Frac(d.X - _scroll * d.Parallax * scale / (w + dw)) * (w + dw) - dw;
            SpriteCache.Draw(dc, d.Sprite, SpriteCache.FrameAt(d.Sprite, t), Math.Round(x / scale) * scale, Math.Round(d.Y * spaceH / scale) * scale, scale);
        }
        // 밤: 하늘을 살짝 푸르게 눌러 별이 더 또렷하게 (고래·별은 그 위에 그대로)
        if (_night > 0)
            using (dc.PushOpacity(0.28 * _night)) dc.FillRectangle(SkinArt.Solid(Palette.NightShade), new Rect(0, 0, w, spaceH));
        _events.DrawBehind(dc, t, scale);

        // 별 (먼 것부터)
        double[] factor = { 0.15, 0.4, 0.9 };
        var dot = SkinArt.Solid(theme.StarDot);
        string small = SpriteLibrary.TryGet(theme.StarSmall, out _) ? theme.StarSmall : "star_small";
        string big = SpriteLibrary.TryGet(theme.StarBig, out _) ? theme.StarBig : "star_big";
        foreach (var star in _stars)
        {
            double x = Frac(star.X - _scroll * factor[star.Layer] * scale / w) * (w + 8 * scale) - 4 * scale;
            double y = Math.Round(star.Y * (spaceH - 6 * scale));
            x = Math.Round(x / scale) * scale;
            switch (star.Layer)
            {
                case 0:
                    dc.FillRectangle(dot, new Rect(x, y, scale, scale));
                    break;
                case 1:
                    SpriteCache.Draw(dc, small, SpriteCache.FrameAt(small, t + star.Phase), x, y, scale);
                    break;
                default:
                    SpriteCache.Draw(dc, big, SpriteCache.FrameAt(big, t + star.Phase), x, y, scale);
                    break;
            }
        }

        var (shipNormal, shipBoost) = s != null ? SkinArt.Ship(s) : ("probe", "probe_boost");
        double probeX = ProbeX(scale);
        var probe = SpriteLibrary.Get(shipNormal);
        double probeY = Math.Round((spaceH - probe.Height * scale) / 2 + Math.Sin(t * 2.2) * 1.5 * scale);

        if (s != null)
        {
            // 출발 직후: 연구소 소행성이 뒤로 멀어진다 (새 항해마다 — 연구소가 새 목적지로 이사해 거기서 떠난다)
            var station = SpriteLibrary.Get("station");
            double stationX = probeX - station.Width * scale - 6 * scale - s.Distance * 0.6 * scale;
            if (stationX > -station.Width * scale)
                SpriteCache.Draw(dc, "station", SpriteCache.FrameAt("station", t), Math.Round(stationX), spaceH - station.Height * scale - 2 * scale, scale);

            DrawUpcoming(dc, s, scale, probeX + probe.Width * scale / 2.0, w, t);
            if (_passingSprite != null && SpriteLibrary.TryGet(_passingSprite, out var passing))
                SpriteCache.Draw(dc, _passingSprite, SpriteCache.FrameAt(_passingSprite, t), Math.Round(_passingX), _passingY - passing.Height * scale / 2.0, scale);
        }

        _events.DrawFront(dc, t, scale);

        foreach (var p in _particles)
        {
            var sparkle = SpriteLibrary.Get("sparkle");
            int frame = Math.Min(sparkle.FrameCount - 1, (int)(p.Age / 0.7 * sparkle.FrameCount));
            SpriteCache.Draw(dc, "sparkle", frame, Math.Round(p.X / scale) * scale, Math.Round(p.Y / scale) * scale, scale, 1 - p.Age / 0.9);
        }

        // 별빛 도착: 탐사선 위로 옅은 빛기둥 + 빨려 드는 반짝이 (탐사선 뒤로 사라진다)
        if (t - _creditedAt < CreditShowerSeconds) DrawCreditShower(dc, t - _creditedAt, scale);

        bool boosting = s != null && GameRules.IsBoosting(s);
        string ship = boosting ? shipBoost : shipNormal;
        var drawn = SpriteLibrary.Get(ship);
        SpriteCache.Draw(dc, ship, SpriteCache.FrameAt(ship, t), probeX, Math.Round(probeY / scale) * scale, scale);
        ShipBounds = new Rect(probeX, Math.Round((spaceH - probe.Height * scale) / 2 / scale) * scale,
            Math.Max(probe.Width, drawn.Width) * scale, probe.Height * scale);

        DrawLab(dc, s, theme, scale, w, h, spaceH, t);

        foreach (var f in _floaters)
        {
            if (!SpriteLibrary.TryGet(f.Sprite, out _)) continue;
            double alpha = Math.Clamp(1 - (f.Age - f.Life * 0.6) / (f.Life * 0.4), 0, 1);
            SpriteCache.Draw(dc, f.Sprite, SpriteCache.FrameAt(f.Sprite, f.Age), Math.Round(f.X / scale) * scale, Math.Round(f.Y / scale) * scale, scale, alpha);
        }

        if (_bubbles.Current is { } bubble)
        {
            int index = bubble.Hamster is int hi ? (hi < 0 ? _captainIndex : hi) : -1;
            var anchor = index >= 0 && index < _hamsterRects.Count
                ? new Point(_hamsterRects[index].X + _hamsterRects[index].Width / 2, _hamsterRects[index].Y - scale)
                : bubble.At;
            // 탐사선은 가리지 않는다 (출렁임만큼 여유를 두고)
            _bubbles.Draw(dc, anchor, w, scale, ShipBounds.Inflate(2 * scale));
        }
    }

    private void DrawCreditShower(DrawingContext dc, double since, int scale)
    {
        var c = ShipCenter(scale);
        var glow = SkinArt.Solid(Palette.Starlight);
        double a = Math.Sin(Math.PI * Math.Clamp(since / CreditShowerSeconds, 0, 1));
        double cx = Math.Round(c.X / scale) * scale;
        double bottom = Math.Round(c.Y / scale) * scale;
        using (dc.PushOpacity(0.10 * a)) dc.FillRectangle(glow, new Rect(cx - 5 * scale, 0, 10 * scale, bottom));
        using (dc.PushOpacity(0.14 * a)) dc.FillRectangle(glow, new Rect(cx - 2 * scale, 0, 4 * scale, bottom));
        foreach (var m in _motes)
        {
            double u = m.Age / m.Life;
            double e = u * u; // 처음엔 둥실, 갈수록 빨리 빨려 든다
            double x = m.X0 + (c.X - m.X0) * e - 2 * scale;
            double y = m.Y0 + (c.Y - m.Y0) * e - 2 * scale;
            int frame = u < 0.8 ? (int)(m.Age * 12) & 1 : 2;
            SpriteCache.Draw(dc, "sparkle", frame, Math.Round(x / scale) * scale, Math.Round(y / scale) * scale, scale, Math.Min(1, u * 5));
        }
    }

    /// <summary>다음 발견 천체: 이정표 구간의 55% 를 지나면 오른쪽에서 들어와 탐사선 쪽으로 다가온다</summary>
    private void DrawUpcoming(DrawingContext dc, GameState s, int scale, double meetX, double w, double t)
    {
        int k = s.DiscoveryCount;
        double prev = GameRules.PrevDiscoveryDistance(s);
        double next = GameRules.NextDiscoveryDistance(s);
        double p = Math.Clamp((s.Distance - prev) / Math.Max(1, next - prev), 0, 1);
        _lastUpcomingX = double.NaN;
        if (p < 0.55) return;
        string id = Destinations.SpriteOf(Destinations.Current(s), k);
        if (!SpriteLibrary.TryGet(id, out var sprite)) return;
        double u = (p - 0.55) / 0.45;
        double eased = 1 - Math.Pow(1 - u, 2);
        double startX = w + 4 * scale;
        double endX = meetX - sprite.Width * scale / 2.0;
        double x = Math.Round((startX + (endX - startX) * eased) / scale) * scale;
        _lastUpcomingX = x;
        double y = ObjectY(k, scale) - sprite.Height * scale / 2.0;
        SpriteCache.Draw(dc, id, SpriteCache.FrameAt(id, t), x, Math.Round(y / scale) * scale, scale, Math.Min(1, u * 3));
    }

    /// <summary>연구소 단면: 벽(창 + 등) · 바닥 · 소품 · 햄찌 크루 (가운데가 선장 — 복장·털색 스킨)</summary>
    private void DrawLab(DrawingContext dc, GameState? s, SceneTheme theme, int scale, double w, double h, double top, double t)
    {
        var line = SkinArt.Solid(theme.Line);
        var window = SkinArt.Solid(theme.Window);
        var lamp = SkinArt.Solid(theme.Lamp);
        var dot = SkinArt.Solid(theme.StarDot);

        dc.FillRectangle(line, new Rect(0, top, w, scale));
        dc.FillRectangle(SkinArt.Solid(theme.Wall), new Rect(0, top + scale, w, h - top - scale));
        double floorTop = h - 5 * scale;
        dc.FillRectangle(SkinArt.Solid(theme.WallDark), new Rect(0, floorTop - 2 * scale, w, 2 * scale));

        // 둥근 창(네모 도트로) — 창 안에도 별이 반짝인다
        for (double wx = 10 * scale; wx < w - 12 * scale; wx += 46 * scale)
        {
            double wy = top + 4 * scale;
            dc.FillRectangle(line, new Rect(wx + scale, wy, 8 * scale, 10 * scale));
            dc.FillRectangle(line, new Rect(wx, wy + scale, 10 * scale, 8 * scale));
            dc.FillRectangle(window, new Rect(wx + 2 * scale, wy + scale, 6 * scale, 8 * scale));
            dc.FillRectangle(window, new Rect(wx + scale, wy + 2 * scale, 8 * scale, 6 * scale));
            if (((int)(t * 1.5 + wx) & 1) == 0) dc.FillRectangle(lamp, new Rect(wx + 6 * scale, wy + 3 * scale, scale, scale));
            dc.FillRectangle(dot, new Rect(wx + 3 * scale, wy + 6 * scale, scale, scale));
            // 창 사이 작은 등
            double lx = wx + 26 * scale;
            if (lx < w - 4 * scale)
            {
                dc.FillRectangle(line, new Rect(lx, top + scale, scale, 3 * scale));
                dc.FillRectangle(lamp, new Rect(lx - scale, top + 4 * scale, 3 * scale, 2 * scale));
            }
        }

        dc.FillRectangle(SkinArt.Solid(theme.FloorLight), new Rect(0, floorTop, w, scale));
        dc.FillRectangle(SkinArt.Solid(theme.Floor), new Rect(0, floorTop + scale, w, h - floorTop - scale));
        var plank = SkinArt.Solid(theme.FloorDark);
        for (double px = 3 * scale; px < w; px += 13 * scale)
            dc.FillRectangle(plank, new Rect(px, floorTop + 2 * scale + ((int)(px / scale) % 2) * scale, scale, 2 * scale));

        if (s != null) LabDecorRenderer.Draw(dc, s, theme, scale, w, top, floorTop, t);
        // 밤: 벽·바닥·소품을 푸르게 낮춘다 (햄찌는 그 위에 — 등불 아래에서 또렷하게)
        if (_night > 0)
            using (dc.PushOpacity(0.36 * _night)) dc.FillRectangle(SkinArt.Solid(Palette.NightShade), new Rect(0, top + scale, w, h - top - scale));

        var (count, spacing, startX, captain) = LabSlots(s, scale);
        _captainIndex = captain;
        _hamsterRects.Clear();
        string captainFur = s != null ? SkinArt.CaptainFur(s) : "hamster_0";
        string? outfit = s != null ? SkinArt.Outfit(s) : null;
        for (int i = 0; i < count && i < _walkers.Count; i++)
        {
            var walker = _walkers[i];
            bool isCaptain = i == captain;
            string fur = isCaptain ? captainFur : SkinArt.CrewFur(captainFur, i < captain ? i : i - 1);

            int frame;
            double lift;
            double sincePet = t - walker.PettedAt;
            if (sincePet < 0.6)
            {
                // 쓰다듬으면 높이 폴짝 + 눈 꼭
                frame = SkinArt.FrameBlink;
                lift = Math.Round(Math.Sin(sincePet / 0.6 * Math.PI) * 6);
            }
            else if (walker.Dozing)
            {
                // 꾸벅꾸벅: 눈 감은 채(깜빡 프레임) 가만히
                frame = SkinArt.FrameBlink;
                lift = 0;
            }
            else if (walker.Walking)
            {
                // 걸음마다 한 칸 통 — 발을 번갈아 내딛는다
                bool stepB = (int)(walker.StepClock / StepSeconds) % 2 == 1;
                frame = stepB ? SkinArt.FrameStepB : SkinArt.FrameStepA;
                lift = stepB ? 1 : 0;
            }
            else
            {
                double cycle = (t + walker.Phase) % 4.3;
                frame = cycle < 0.15 ? SkinArt.FrameBlink : SkinArt.FrameIdle;
                // 가끔 제자리 폴짝
                double hop = (t + walker.Phase * 1.7) % (5.0 + i % 3);
                lift = hop < 0.45 ? Math.Round(Math.Sin(hop / 0.45 * Math.PI) * 3) : 0;
            }
            double x = Math.Round((startX + i * spacing) / scale + walker.X) * scale;
            double y = floorTop - 16 * scale + scale - lift * scale;
            _hamsterRects.Add(new Rect(x, y, 16 * scale, 16 * scale));
            SkinArt.DrawHamster(dc, fur, isCaptain ? outfit : null, frame, x, y, scale, t, walker.Left);
        }
        if (_night > 0) DrawNightLamps(dc, theme, scale, w, h, top, floorTop);
    }

    /// <summary>
    /// 밤의 등불: 연구소 전체를 한 겹 더 살짝 낮춘 뒤, 작은 등마다 따뜻한 빛 무리 + 아래로 계단처럼 퍼지는 빛 + 바닥의 빛 웅덩이.
    /// 등 자리는 DrawLab 의 벽 규칙과 같다 (창 x = 10 + 46k, 등 x = 창 + 26).
    /// </summary>
    private void DrawNightLamps(DrawingContext dc, SceneTheme theme, int scale, double w, double h, double top, double floorTop)
    {
        double n = _night;
        var glow = SkinArt.Solid(Palette.LampGlow);
        using (dc.PushOpacity(0.10 * n)) dc.FillRectangle(SkinArt.Solid(Palette.NightShade), new Rect(0, top + scale, w, h - top - scale));
        double coneTop = top + 6 * scale;
        int rows = Math.Max(1, (int)Math.Ceiling((floorTop - coneTop) / (2 * scale)));
        for (double wx = 10 * scale; wx < w - 12 * scale; wx += 46 * scale)
        {
            double lx = wx + 26 * scale;
            if (lx >= w - 4 * scale) continue;
            using (dc.PushOpacity(0.40 * n)) dc.FillRectangle(glow, new Rect(lx - 2 * scale, top + 3 * scale, 5 * scale, 4 * scale));
            for (int k = 0; k < rows; k++)
            {
                double y = coneTop + k * 2 * scale;
                double rowH = Math.Min(2 * scale, floorTop - y);
                if (rowH <= 0) break;
                double half = (1 + k) * scale;
                using (dc.PushOpacity(0.30 * n * (1 - 0.55 * k / rows)))
                    dc.FillRectangle(glow, new Rect(lx - scale - half, y, 3 * scale + 2 * half, rowH));
            }
            double pool = (1 + rows) * scale;
            using (dc.PushOpacity(0.30 * n)) dc.FillRectangle(glow, new Rect(lx - scale - pool, floorTop, 3 * scale + 2 * pool, scale));
            // 등 자체는 그늘 없이 환하게
            dc.FillRectangle(SkinArt.Solid(theme.Lamp), new Rect(lx - scale, top + 4 * scale, 3 * scale, 2 * scale));
        }
    }

    private static double Frac(double v) => v - Math.Floor(v);
}
