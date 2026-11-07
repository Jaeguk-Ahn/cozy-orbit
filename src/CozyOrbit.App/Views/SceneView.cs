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
/// 배율은 높이로 정한다 (플로팅 창처럼 작으면 2, 큰 창이면 3). 좌표는 "도트 단위" × 정수 배율.
/// </summary>
public sealed class SceneView : Control
{
    public GameHost? Host { get; set; }

    /// <summary>발견·별사탕 같은 알림을 선장 말풍선으로 보여 줄지 (큰 창은 토스트가 있으므로 끈다)</summary>
    public bool ShowEventBubbles { get; set; }

    private const int LabHeightUnits = 22;
    private const double FrameInterval = 1.0 / 30;
    /// <summary>햄찌 걷는 속도 (도트 단위/초)</summary>
    private const double WalkSpeed = 7;
    /// <summary>걸음 프레임 하나의 길이</summary>
    private const double StepSeconds = 0.16;

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

    // 지나가는 천체 (발견 직후 왼쪽으로 빠져나간다)
    private int _seenDiscoveries = -1;
    private int _seenVoyage = -1;
    private string? _passingSprite;
    private double _passingX;
    private double _passingY;
    private double _lastUpcomingX = double.NaN;

    private readonly record struct Star(double X, double Y, int Layer, double Phase);
    private sealed class Particle { public double X, Y, Age; }
    /// <summary>위로 둥실 떠오르는 작은 그림 (하트·음표·반짝이)</summary>
    private sealed class Floater { public required string Sprite; public double X, Y, Age, Life = 1.2, Drift; }

    /// <summary>돌아다니는 햄찌 하나. X·Target 은 제자리(칸 가운데)로부터의 거리 (도트 단위).</summary>
    private sealed class Walker
    {
        public double X, Target, Wait, StepClock, Phase;
        /// <summary>쓰다듬은 시각 (높이 폴짝)</summary>
        public double PettedAt = double.NegativeInfinity;
        public bool Left;
        public bool Walking => Wait <= 0 && Math.Abs(Target - X) > 1e-6;
    }

    private static readonly (string Ko, string En)[] PetLines =
    [
        ("찍!", "Squeak!"), ("간질간질~", "Tickles~"), ("도토리 최고!", "Acorns rule!"), ("오늘도 같이 힘내요!", "Let's do our best today!"),
        ("해바라기씨 주세요", "Sunflower seeds, please"), ("우주 최고 연구소!", "Best lab in space!"), ("헤헤", "Hehe"),
        ("별이 반짝반짝", "Twinkle twinkle"), ("일 열심히 하고 있어요?", "Working hard?"), ("쉬엄쉬엄 해요~", "Take it easy~"),
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
        if (candy >= 0.1) Notify(L.T($"별사탕 +{Fmt.Number(candy, 1)}", $"Star candy +{Fmt.Number(candy, 1)}"), "icon_candy");
        else if (c.StarlightAdded > 0) Notify(L.T($"별빛 +{Fmt.Duration(c.StarlightAdded)}", $"Starlight +{Fmt.Duration(c.StarlightAdded)}"), "icon_starlight");
    }

    private void OnReturned(TickResult r) =>
        Notify(L.T($"다녀오셨어요! {Fmt.Steps(r.DistanceGained)} 왔어요", $"Welcome back! We flew {Fmt.Steps(r.DistanceGained)}"),
            r.Discoveries.Count > 0 ? r.Discoveries[^1].Sprite : null);

    private void OnVoyageReady() => Notify(L.T("도감 완성! 새 항해를 떠날 수 있어요", "Atlas complete! A new voyage awaits"), "icon_voyage");

    private void OnVoyageStarted(DestinationDef d, double candy) => Notify(L.T($"{d.Name}(으)로 출발!", $"Off to {d.Name}!"), d.Sprite);

    // ── 진행 ──

    private int Scale => Math.Max(2, (int)Math.Floor(Bounds.Height / 86));

    private void Advance()
    {
        if (!IsEffectivelyVisible) return;
        double t = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(t - _lastT, 0, 0.25);
        _lastT = t;

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
            f.Y -= 14 * scale * dt;
            f.X += f.Drift * scale * dt;
        }
        _floaters.RemoveAll(f => f.Age >= f.Life);

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

    /// <summary>햄찌마다 잠깐 쉬었다가 제자리 근처의 새 지점으로 걸어간다</summary>
    private void WalkCrew(GameState? s, int scale, double dt, double t)
    {
        var (count, spacing, _, _) = LabSlots(s, scale);
        while (_walkers.Count < count)
            _walkers.Add(new Walker { Wait = 0.5 + _rng.NextDouble() * 3, Phase = _rng.NextDouble() * 10, Left = _rng.Next(2) == 0 });
        if (_walkers.Count > count) _walkers.RemoveRange(count, _walkers.Count - count);

        // 혼자면 바닥 가운데를 넓게, 여럿이면 제 칸 안에서만 (서로 겹치지 않게)
        double range = count == 1 ? Math.Min(48, Bounds.Width / scale / 2 - 14) : Math.Min(26, spacing / scale / 2 - 9);
        range = Math.Max(0, range);
        foreach (var w in _walkers)
        {
            if (w.Wait > 0)
            {
                w.Wait -= dt;
                if (w.Wait <= 0) w.Target = Math.Round((_rng.NextDouble() * 2 - 1) * range);
                continue;
            }
            double gap = w.Target - w.X;
            double step = WalkSpeed * dt;
            if (Math.Abs(gap) <= step)
            {
                w.X = w.Target;
                w.StepClock = 0;
                w.Wait = 1.5 + _rng.NextDouble() * 4;
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
            for (int i = 0; i < 4; i++)
                _floaters.Add(new Floater { Sprite = "sparkle", X = star.X + (_rng.NextDouble() * 8 - 4) * scale, Y = star.Y, Drift = _rng.NextDouble() * 10 - 5, Life = 0.8 });
            _bubbles.Interrupt(new SceneBubbles.Bubble { Text = L.T($"별가루 +{Fmt.Compact(reward)}!", $"Stardust +{Fmt.Compact(reward)}!"), Icon = "icon_stardust", At = new Point(star.X, star.Y), Duration = 1.8 });
            e.Handled = true;
            return;
        }
        if (_events.HitWhale(p, scale))
        {
            _bubbles.Interrupt(new SceneBubbles.Bubble { Text = L.T("뿌우~ 안녕!", "Fwoo~ hello!"), At = p, Duration = 2 });
            _floaters.Add(new Floater { Sprite = "fx_note", X = p.X, Y = p.Y, Drift = 3 });
            e.Handled = true;
            return;
        }
        for (int i = 0; i < _hamsterRects.Count && i < _walkers.Count; i++)
        {
            if (!_hamsterRects[i].Inflate(2 * scale).Contains(p)) continue;
            var w = _walkers[i];
            w.Target = w.X;
            w.Wait = Math.Max(w.Wait, 1.4);
            w.PettedAt = t;
            var r = _hamsterRects[i];
            _floaters.Add(new Floater { Sprite = "fx_heart", X = r.X + r.Width / 2 - 3 * scale, Y = r.Y, Drift = _rng.NextDouble() * 6 - 3 });
            if (_rng.Next(3) == 0) _floaters.Add(new Floater { Sprite = "fx_note", X = r.X + r.Width / 2 + 2 * scale, Y = r.Y + 2 * scale, Drift = 4, Life = 1.4 });
            var line = PetLines[_rng.Next(PetLines.Length)];
            _bubbles.Interrupt(new SceneBubbles.Bubble { Text = L.T(line.Ko, line.En), Hamster = i, Duration = 2.2 });
            host.PetHamster();
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

        bool boosting = s != null && GameRules.IsBoosting(s);
        string ship = boosting ? shipBoost : shipNormal;
        SpriteCache.Draw(dc, ship, SpriteCache.FrameAt(ship, t), probeX, Math.Round(probeY / scale) * scale, scale);

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
            _bubbles.Draw(dc, anchor, w, scale);
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
    }

    private static double Frac(double v) => v - Math.Floor(v);
}
