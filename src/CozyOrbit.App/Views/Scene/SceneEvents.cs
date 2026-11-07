using Avalonia;
using Avalonia.Media;
using CozyOrbit.App.Rendering;
using CozyOrbit.Core.Art;

namespace CozyOrbit.App.Views.Scene;

/// <summary>
/// 가끔 일어나는 우주 이벤트 (docs/design.md §12) — 장면마다 따로 돌고, 보상은 누르면 GameHost 가 준다.
/// - 별똥별: 3~8분마다 하나 (처음은 1~2분 뒤). 누르면 별가루.
/// - 유성우: 45~90분마다 25초 동안 작은 별똥이 쏟아진다 (처음은 20~40분 뒤). 하나하나 누를 수 있다.
/// - 우주 고래: 20~40분마다 배경을 천천히 가로지른다 (처음은 8~15분 뒤). 누르면 인사.
/// </summary>
public sealed class SceneEvents
{
    public sealed class FallingStar
    {
        public double X, Y, VX, VY, Age, Life;
        public bool Small;
        public string Sprite => Small ? "fx_meteor" : "fx_shooting_star";
    }

    public sealed class Whale
    {
        public double X, Y, Speed;
    }

    private const double ShowerSeconds = 25;
    private readonly Random _rng;
    private readonly List<FallingStar> _stars = new();
    private double _nextStar, _nextShower, _nextWhale, _showerUntil = -1, _nextShowerDrop;

    public Whale? CurrentWhale { get; private set; }
    public IReadOnlyList<FallingStar> Stars => _stars;
    public bool ShowerActive(double t) => t < _showerUntil;

    public SceneEvents(Random rng, double now)
    {
        _rng = rng;
        _nextStar = now + Between(60, 120);
        _nextShower = now + Between(20 * 60, 40 * 60);
        _nextWhale = now + Between(8 * 60, 15 * 60);
    }

    private double Between(double a, double b) => a + _rng.NextDouble() * (b - a);

    /// <summary>진행. 좌표는 px, 크기 계산은 도트 배율(scale) 기준.</summary>
    public void Update(double t, double dt, double width, double spaceHeight, int scale)
    {
        if (t >= _nextStar)
        {
            Spawn(width, spaceHeight, scale, small: false);
            _nextStar = t + Between(180, 480);
        }
        if (t >= _nextShower)
        {
            _showerUntil = t + ShowerSeconds;
            _nextShowerDrop = t;
            _nextShower = t + Between(45 * 60, 90 * 60);
        }
        if (t < _showerUntil && t >= _nextShowerDrop)
        {
            Spawn(width, spaceHeight, scale, small: true);
            _nextShowerDrop = t + Between(0.35, 0.6);
        }
        if (CurrentWhale == null && t >= _nextWhale)
        {
            CurrentWhale = new Whale { X = width + 4 * scale, Y = Math.Round(spaceHeight * Between(0.12, 0.32)), Speed = width / Between(26, 34) };
            _nextWhale = t + Between(20 * 60, 40 * 60);
        }

        foreach (var s in _stars)
        {
            s.Age += dt;
            s.X += s.VX * dt;
            s.Y += s.VY * dt;
        }
        _stars.RemoveAll(s => s.Age >= s.Life || s.X < -20 * scale || s.Y > spaceHeight);

        if (CurrentWhale is { } whale)
        {
            whale.X -= whale.Speed * dt;
            if (whale.X < -48 * scale) CurrentWhale = null;
        }
    }

    /// <summary>오른쪽 위에서 왼쪽 아래로 비스듬히 떨어진다</summary>
    private void Spawn(double width, double spaceHeight, int scale, bool small)
    {
        double speed = (small ? Between(70, 100) : Between(55, 75)) * scale;
        double angle = Between(0.45, 0.7); // 아래쪽으로 기운 정도 (라디안)
        _stars.Add(new FallingStar
        {
            X = width * Between(0.45, 1.0),
            Y = spaceHeight * Between(0.02, 0.35),
            VX = -Math.Cos(angle) * speed,
            VY = Math.Sin(angle) * speed,
            Life = small ? Between(1.2, 1.8) : Between(2.2, 2.8),
            Small = small,
        });
    }

    /// <summary>배경 (테마 장식 다음, 별 앞) — 고래</summary>
    public void DrawBehind(DrawingContext dc, double t, int scale)
    {
        if (CurrentWhale is not { } whale || !SpriteLibrary.TryGet("fx_space_whale", out _)) return;
        double bob = Math.Round(Math.Sin(t * 1.3) * 2) * scale;
        SpriteCache.Draw(dc, "fx_space_whale", SpriteCache.FrameAt("fx_space_whale", t), Math.Round(whale.X / scale) * scale, whale.Y + bob, scale, 0.9);
    }

    /// <summary>앞 (탐사선 뒤) — 별똥별</summary>
    public void DrawFront(DrawingContext dc, double t, int scale)
    {
        foreach (var s in _stars)
        {
            double fade = Math.Min(1, (s.Life - s.Age) / 0.4);
            if (SpriteLibrary.TryGet(s.Sprite, out _))
            {
                SpriteCache.Draw(dc, s.Sprite, SpriteCache.FrameAt(s.Sprite, t), Math.Round(s.X / scale) * scale, Math.Round(s.Y / scale) * scale, scale, fade);
            }
            else
            {
                // 그림이 아직 없으면 점 하나 + 꼬리
                var b = SkinArt.Solid(Palette.Butter);
                dc.FillRectangle(b, new Rect(Math.Round(s.X), Math.Round(s.Y), 2 * scale, 2 * scale));
            }
        }
    }

    /// <summary>누른 점에 있는 별똥별 (넉넉하게 판정)</summary>
    public FallingStar? HitStar(Point p, int scale)
    {
        for (int i = _stars.Count - 1; i >= 0; i--)
        {
            var s = _stars[i];
            var size = SpriteLibrary.TryGet(s.Sprite, out var sp) ? new Size(sp.Width * scale, sp.Height * scale) : new Size(4 * scale, 4 * scale);
            var hit = new Rect(s.X - 6 * scale, s.Y - 6 * scale, size.Width + 12 * scale, size.Height + 12 * scale);
            if (hit.Contains(p)) return s;
        }
        return null;
    }

    public void Remove(FallingStar s) => _stars.Remove(s);

    public bool HitWhale(Point p, int scale)
    {
        if (CurrentWhale is not { } whale || !SpriteLibrary.TryGet("fx_space_whale", out var sp)) return false;
        return new Rect(whale.X, whale.Y, sp.Width * scale, sp.Height * scale).Inflate(4 * scale).Contains(p);
    }
}
