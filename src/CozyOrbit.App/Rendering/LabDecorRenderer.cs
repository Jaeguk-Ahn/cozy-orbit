using Avalonia;
using Avalonia.Media;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;

namespace CozyOrbit.App.Rendering;

/// <summary>
/// 연구소에 놓은 소품을 그린다 (햄찌보다 먼저 — 햄찌가 앞으로 지나간다).
/// 벽 장식 배치는 SceneView.DrawLab 과 같은 규칙: 창은 x = 10 + 46k (너비 10), 작은 등은 x = 36 + 46k (너비 3, 35~37).
/// 벽 자리 = 창 오른쪽 ~ 등 사이 [20 + 46k, 35 + 46k), 바닥 자리 = 등 ~ 다음 창 사이 [38 + 46k, 56 + 46k) + 왼쪽 구석.
/// 벽·바닥 자리가 겹치지 않아 키 큰 바닥 소품도 벽 소품을 가리지 않는다. 가운데에 가까운 자리부터 놓은 순서대로 쓴다.
/// </summary>
public static class LabDecorRenderer
{
    private const int Period = 46;
    private const int WallGapStart = 20, WallGapEnd = 35;
    private const int FloorGapStart = 38, FloorGapEnd = 56;
    /// <summary>창 왼쪽 구석 바닥 자리 [1, 19) 의 가운데</summary>
    private const double CornerFloorCenter = 10;

    public static void Draw(DrawingContext dc, GameState s, SceneTheme theme, int scale, double width, double labTop, double floorTop, double t)
    {
        var wall = Decor.Placed(s, DecorKind.Wall);
        var floor = Decor.Placed(s, DecorKind.Floor);
        if (wall.Count == 0 && floor.Count == 0) return;

        int units = (int)Math.Floor(width / scale);
        var (wallSlots, floorSlots) = Slots(units);

        // 벽: 위 선(1칸) 아래부터 바닥 위 어두운 띠(2칸) 전까지의 가운데에 건다
        int wallArea = (int)Math.Round((floorTop - labTop) / scale) - 3;
        for (int i = 0; i < wall.Count && i < wallSlots.Count; i++)
        {
            string id = wall[i].Sprite;
            if (!SpriteLibrary.TryGet(id, out var sprite)) continue;
            int x = Left(wallSlots[i], sprite.Width, units);
            int y = 1 + Math.Max(0, (wallArea - sprite.Height) / 2);
            SpriteCache.Draw(dc, id, SpriteCache.FrameAt(id, t + i * 0.37), x * scale, labTop + y * scale, scale);
        }

        // 바닥: 맨 아래 행이 바닥 밝은 줄(floorTop)에 닿는다 — 햄찌와 같은 높이. 아래 바닥에 그림자 한 줄.
        var shadow = SkinArt.Solid(theme.FloorDark);
        for (int i = 0; i < floor.Count && i < floorSlots.Count; i++)
        {
            string id = floor[i].Sprite;
            if (!SpriteLibrary.TryGet(id, out var sprite)) continue;
            int x = Left(floorSlots[i], sprite.Width, units);
            if (sprite.Width > 4)
                dc.FillRectangle(shadow, new Rect((x + 2) * scale, floorTop + scale, (sprite.Width - 4) * scale, scale));
            SpriteCache.Draw(dc, id, SpriteCache.FrameAt(id, t + 0.5 + i * 0.37), x * scale, floorTop + (1 - sprite.Height) * scale, scale);
        }
    }

    /// <summary>가운데 정렬한 왼쪽 칸 (화면 밖으로 나가지 않게)</summary>
    private static int Left(double center, int spriteWidth, int units) =>
        Math.Clamp((int)Math.Floor(center - spriteWidth / 2.0 + 0.5), 0, Math.Max(0, units - spriteWidth));

    /// <summary>너비(칸) → 자리. 큰 창·플로팅 창이 번갈아 그리므로 너비마다 한 번만 계산한다 (UI 스레드 전용).</summary>
    private static readonly Dictionary<int, (List<double> Wall, List<double> Floor)> SlotCache = new();

    /// <summary>너비(칸)에 맞는 벽·바닥 자리 가운데 x — 장면 가운데에 가까운 순서로 최대 3개씩</summary>
    private static (List<double> Wall, List<double> Floor) Slots(int units)
    {
        if (SlotCache.TryGetValue(units, out var cached)) return cached;
        var wall = new List<double>();
        var floor = new List<double>();
        for (int k = 0; WallGapEnd + k * Period <= units; k++)
            wall.Add((WallGapStart + WallGapEnd) / 2.0 + k * Period);
        for (int k = 0; FloorGapEnd + k * Period <= units; k++)
            floor.Add((FloorGapStart + FloorGapEnd) / 2.0 + k * Period);
        if (units >= 20) floor.Add(CornerFloorCenter);

        double mid = units / 2.0;
        var wallSlots = wall.OrderBy(c => Math.Abs(c - mid)).ThenBy(c => c).Take(Decor.MaxPerKind).ToList();
        var floorSlots = floor.OrderBy(c => Math.Abs(c - mid)).ThenBy(c => c).Take(Decor.MaxPerKind).ToList();

        // 좁은 창: 벽 자리가 모자라면 바닥 자리 위 벽에 건다 (안 쓰는 바닥 자리 먼저 — 쓰는 자리면 키 큰 바닥 소품이 앞을 조금 가릴 수 있다)
        foreach (var c in floor.OrderBy(floorSlots.Contains).ThenBy(c => Math.Abs(c - mid)))
        {
            if (wallSlots.Count >= Decor.MaxPerKind) break;
            wallSlots.Add(c);
        }
        return SlotCache[units] = (wallSlots, floorSlots);
    }
}
