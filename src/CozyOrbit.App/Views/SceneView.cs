using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.App.Views;

/// <summary>
/// 우주 장면: 테마 하늘·장식 + 별 3겹 시차 스크롤 + 탐사선(스킨) + 다가오는/지나가는 천체 + 부스트 반짝이.
/// 아래에는 연구소 바닥과 햄찌 크루를 그린다 — 햄찌들은 제자리 근처를 좌우로 돌아다닌다.
/// 배율은 높이로 정한다 (플로팅 창처럼 작으면 2, 큰 창이면 3).
/// 모든 좌표는 "도트 단위" × 정수 배율로 계산해 픽셀이 어긋나지 않게 한다.
/// </summary>
public sealed class SceneView : Control
{
    public GameHost? Host { get; set; }

    private const int LabHeightUnits = 22;
    private const double FrameInterval = 1.0 / 30;
    /// <summary>햄찌 걷는 속도 (도트 단위/초)</summary>
    private const double WalkSpeed = 7;
    /// <summary>걸음 프레임 하나의 길이</summary>
    private const double StepSeconds = 0.16;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new(1107);
    private readonly Star[] _stars;
    private readonly List<Particle> _particles = new();
    private readonly List<Walker> _walkers = new();
    private double _lastT;
    private double _scroll;
    private double _visualSpeed = 8;
    private double _sparkleDue;

    // 지나가는 천체 (발견 직후 왼쪽으로 빠져나간다)
    private int _seenDiscoveries = -1;
    private string? _passingSprite;
    private double _passingX;
    private double _passingY;
    private double _lastUpcomingX = double.NaN;

    private readonly record struct Star(double X, double Y, int Layer, double Phase);
    private sealed class Particle { public double X, Y, Age; }

    /// <summary>돌아다니는 햄찌 하나. X·Target 은 제자리(칸 가운데)로부터의 거리 (도트 단위).</summary>
    private sealed class Walker
    {
        public double X, Target, Wait, StepClock, Phase;
        public bool Left;
        public bool Walking => Wait <= 0 && Math.Abs(Target - X) > 1e-6;
    }

    public SceneView()
    {
        ClipToBounds = true;
        _stars = Enumerable.Range(0, 70).Select(i => new Star(_rng.NextDouble(), _rng.NextDouble(), i < 44 ? 0 : i < 64 ? 1 : 2, _rng.NextDouble() * 10)).ToArray();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(FrameInterval), DispatcherPriority.Render, (_, _) => Advance());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastT = _clock.Elapsed.TotalSeconds;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

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

        // 발견이 일어나면 다가오던 천체를 "지나가는" 천체로 넘긴다
        if (s != null)
        {
            if (_seenDiscoveries >= 0 && s.DiscoveryCount > _seenDiscoveries)
            {
                _passingSprite = Discoveries.SpriteOf(s.DiscoveryCount - 1);
                _passingX = double.IsNaN(_lastUpcomingX) ? probeX : _lastUpcomingX;
                _passingY = ObjectY(s.DiscoveryCount - 1, scale);
            }
            _seenDiscoveries = s.DiscoveryCount;
        }
        if (_passingSprite != null)
        {
            _passingX -= _visualSpeed * 1.6 * scale * dt;
            if (_passingX < -40 * scale) _passingSprite = null;
        }

        WalkCrew(s, scale, dt);

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
    private void WalkCrew(GameState? s, int scale, double dt)
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
            // 출발 직후: 연구소 소행성이 뒤로 멀어진다
            var station = SpriteLibrary.Get("station");
            double stationX = probeX - station.Width * scale - 6 * scale - s.Distance * 0.6 * scale;
            if (stationX > -station.Width * scale)
                SpriteCache.Draw(dc, "station", SpriteCache.FrameAt("station", t), Math.Round(stationX), spaceH - station.Height * scale - 2 * scale, scale);

            DrawUpcoming(dc, s, scale, probeX + probe.Width * scale / 2.0, w, t);
            if (_passingSprite != null && SpriteLibrary.TryGet(_passingSprite, out var passing))
                SpriteCache.Draw(dc, _passingSprite, SpriteCache.FrameAt(_passingSprite, t), Math.Round(_passingX), _passingY - passing.Height * scale / 2.0, scale);
        }

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
    }

    /// <summary>다음 발견 천체: 이정표 구간의 55% 를 지나면 오른쪽에서 들어와 탐사선 쪽으로 다가온다</summary>
    private void DrawUpcoming(DrawingContext dc, GameState s, int scale, double meetX, double w, double t)
    {
        int k = s.DiscoveryCount;
        double prev = k == 0 ? 0 : Balance.DiscoveryDistance(k - 1);
        double next = Balance.DiscoveryDistance(k);
        double p = Math.Clamp((s.Distance - prev) / Math.Max(1, next - prev), 0, 1);
        _lastUpcomingX = double.NaN;
        if (p < 0.55) return;
        string id = Discoveries.SpriteOf(k);
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

    /// <summary>연구소 단면: 벽(창 + 등) · 바닥 · 햄찌 크루 (가운데가 선장 — 복장·털색 스킨)</summary>
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

        var (count, spacing, startX, captain) = LabSlots(s, scale);
        string captainFur = s != null ? SkinArt.CaptainFur(s) : "hamster_0";
        string? outfit = s != null ? SkinArt.Outfit(s) : null;
        for (int i = 0; i < count && i < _walkers.Count; i++)
        {
            var walker = _walkers[i];
            bool isCaptain = i == captain;
            string fur = isCaptain ? captainFur : SkinArt.CrewFur(captainFur, i < captain ? i : i - 1);

            int frame;
            double lift;
            if (walker.Walking)
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
            SkinArt.DrawHamster(dc, fur, isCaptain ? outfit : null, frame, x, y, scale, t, walker.Left);
        }
    }

    private static double Frac(double v) => v - Math.Floor(v);
}
