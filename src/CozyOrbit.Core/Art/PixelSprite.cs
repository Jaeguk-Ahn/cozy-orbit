namespace CozyOrbit.Core.Art;

/// <summary>
/// 도트 스프라이트 한 장 (프레임 여러 개). 픽셀은 0xAARRGGBB 직선 알파, 0 = 투명.
/// 데이터만 들고 있다 — 화면용 비트맵 변환은 앱 쪽(SpriteCache)이 한다.
/// </summary>
public sealed class PixelSprite
{
    private readonly uint[][] _frames;

    internal PixelSprite(string id, int width, int height, double frameSeconds, uint[][] frames)
    {
        Id = id;
        Width = width;
        Height = height;
        FrameSeconds = frameSeconds;
        _frames = frames;
    }

    public string Id { get; }
    public int Width { get; }
    public int Height { get; }
    public int FrameCount => _frames.Length;

    /// <summary>반복 애니메이션에서 프레임 하나가 보이는 시간 (초)</summary>
    public double FrameSeconds { get; }

    public uint Pixel(int frame, int x, int y)
    {
        if ((uint)frame >= (uint)_frames.Length) throw new ArgumentOutOfRangeException(nameof(frame), $"{Id}: 프레임 {frame} / {_frames.Length}");
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x), $"{Id}: x {x} / {Width}");
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y), $"{Id}: y {y} / {Height}");
        return _frames[frame][y * Width + x];
    }
}
