using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CozyOrbit.Core.Art;

namespace CozyOrbit.App.Rendering;

/// <summary>
/// Core 의 도트 스프라이트를 1배 크기 비트맵으로 바꿔 (id, 프레임) 별로 캐시하고, 최근접 보간으로만 그린다.
/// UI 스레드 전용 — 렌더링 경로에서만 부른다.
/// </summary>
public static class SpriteCache
{
    private static readonly Dictionary<(string Id, int Frame), WriteableBitmap> Bitmaps = new();

    /// <summary>1배 비트맵 (Bgra8888, 미리 곱한 알파). 프레임 번호는 프레임 수로 감싼다.</summary>
    public static Bitmap Frame(string id, int frame)
    {
        var sprite = SpriteLibrary.Get(id);
        frame = Wrap(frame, sprite.FrameCount);
        if (Bitmaps.TryGetValue((id, frame), out var cached)) return cached;
        var bitmap = Build(sprite, frame);
        Bitmaps[(id, frame)] = bitmap;
        return bitmap;
    }

    /// <summary>시각 t(초)에 보일 반복 애니메이션 프레임 번호</summary>
    public static int FrameAt(string id, double seconds)
    {
        var sprite = SpriteLibrary.Get(id);
        if (sprite.FrameCount == 1 || !double.IsFinite(seconds) || seconds < 0) return 0;
        // 큰 시각에서도 정밀도가 흐트러지지 않게 한 바퀴 길이로 먼저 나눈다
        var loop = sprite.FrameSeconds * sprite.FrameCount;
        var t = seconds % loop;
        return Math.Min((int)(t / sprite.FrameSeconds), sprite.FrameCount - 1);
    }

    /// <summary>
    /// (x, y) 를 왼쪽 위로 하여 scale 배로 그린다. 최근접 보간이라 번지지 않는다 — 정수 배율일 때 가장 선명하다.
    /// flipX 면 같은 자리에서 좌우로 뒤집어 그린다 (돌아다니는 햄찌).
    /// </summary>
    public static void Draw(DrawingContext dc, string id, int frame, double x, double y, double scale, double opacity = 1, bool flipX = false)
    {
        if (opacity <= 0 || scale <= 0) return;
        var bitmap = Frame(id, frame);
        var size = bitmap.PixelSize;
        var src = new Rect(0, 0, size.Width, size.Height);
        var dest = new Rect(x, y, size.Width * scale, size.Height * scale);
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        // X → (2x + w) − X : dest 안에서 좌우만 바뀐다
        using (flipX ? dc.PushTransform(new Matrix(-1, 0, 0, 1, 2 * x + dest.Width, 0)) : default(DrawingContext.PushedState?))
        using (opacity < 1 ? dc.PushOpacity(opacity) : default(DrawingContext.PushedState?))
        {
            dc.DrawImage(bitmap, src, dest);
        }
    }

    private static int Wrap(int frame, int count) => ((frame % count) + count) % count;

    private static WriteableBitmap Build(PixelSprite sprite, int frame)
    {
        var bitmap = new WriteableBitmap(new PixelSize(sprite.Width, sprite.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var row = new int[sprite.Width];
        for (var y = 0; y < sprite.Height; y++)
        {
            for (var x = 0; x < sprite.Width; x++) row[x] = (int)Premultiply(sprite.Pixel(frame, x, y));
            // 리틀 엔디언에서 0xAARRGGBB 정수는 메모리상 B,G,R,A 순서 = Bgra8888
            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, sprite.Width);
        }
        return bitmap;
    }

    private static uint Premultiply(uint argb)
    {
        var a = argb >> 24;
        if (a == 0xFF) return argb;
        if (a == 0) return 0;
        var r = ((argb >> 16) & 0xFF) * a / 255;
        var g = ((argb >> 8) & 0xFF) * a / 255;
        var b = (argb & 0xFF) * a / 255;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
