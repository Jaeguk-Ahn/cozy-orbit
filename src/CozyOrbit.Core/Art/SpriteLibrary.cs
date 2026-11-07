using System.Diagnostics.CodeAnalysis;

namespace CozyOrbit.Core.Art;

/// <summary>
/// 모든 스프라이트의 문자 격자 + 팔레트. 처음 쓸 때 한 번 해석하고 검증한다 (행 너비·프레임 크기·문자 매핑).
/// 격자: 프레임 하나 = 행 문자열 배열, 한 글자 = 픽셀 하나, '.' = 투명.
/// 데이터는 종류별 partial 파일(Characters·Objects·Icons·Scene)에 나눠 둔다.
/// </summary>
public static partial class SpriteLibrary
{
    private static readonly Lazy<Dictionary<string, PixelSprite>> Sprites = new(Build);

    public static PixelSprite Get(string id) =>
        TryGet(id, out var sprite) ? sprite : throw new KeyNotFoundException($"스프라이트 없음: {id}");

    public static bool TryGet(string id, [NotNullWhen(true)] out PixelSprite? sprite) =>
        Sprites.Value.TryGetValue(id, out sprite);

    public static IReadOnlyCollection<PixelSprite> All => Sprites.Value.Values;

    private static Dictionary<string, PixelSprite> Build()
    {
        var set = new Dictionary<string, PixelSprite>(StringComparer.Ordinal);
        AddCharacters(set);
        AddShips(set);
        AddScene(set);
        AddThemes(set);
        AddObjects(set);
        AddIcons(set);
        return set;
    }

    /// <summary>모든 스프라이트가 같이 쓰는 기본 문자 → 색 매핑. 스프라이트마다 With 로 덧씌울 수 있다.</summary>
    private static readonly Dictionary<char, uint> Base = new()
    {
        ['k'] = Palette.Outline,
        ['K'] = Palette.OutlineSoft,
        ['w'] = Palette.White,
        ['c'] = Palette.Cream,
        ['y'] = Palette.Butter,
        ['g'] = Palette.Gold,
        ['a'] = Palette.Apricot,
        ['o'] = Palette.Warn,
        ['t'] = Palette.Tan,
        ['b'] = Palette.Brown,
        ['d'] = Palette.BrownDark,
        ['p'] = Palette.Pink,
        ['r'] = Palette.Rose,
        ['R'] = Palette.Berry,
        ['v'] = Palette.Stardust,
        ['l'] = Palette.Lilac,
        ['u'] = Palette.Purple,
        ['s'] = Palette.Sky,
        ['e'] = Palette.Blue,
        ['m'] = Palette.Mint,
        ['T'] = Palette.Teal,
        ['h'] = Palette.GrayLight,
        ['i'] = Palette.Gray,
        ['j'] = Palette.GrayDark,
        ['n'] = Palette.PanelLight,
        ['N'] = Palette.SpaceDeep,
    };

    /// <summary>기본 매핑에 몇 글자를 더하거나 바꾼 매핑</summary>
    private static Dictionary<char, uint> With(params (char Key, uint Color)[] extra)
    {
        var map = new Dictionary<char, uint>(Base);
        foreach (var (key, color) in extra) map[key] = color;
        return map;
    }

    /// <summary>프레임 일부 행만 바꾼 새 프레임 (눈 깜빡임처럼 작은 차이만 있는 프레임용)</summary>
    private static string[] Patch(string[] frame, params (int Row, string Text)[] rows)
    {
        var copy = (string[])frame.Clone();
        foreach (var (row, text) in rows) copy[row] = text;
        return copy;
    }

    /// <summary>프레임 전체를 세로로 dy 칸 민 새 프레임 (음수 = 위로, 통통 튀는 대기 동작용). 빈 자리는 투명.</summary>
    private static string[] Shift(string[] frame, int dy)
    {
        var blank = new string('.', frame[0].Length);
        var copy = new string[frame.Length];
        for (var y = 0; y < frame.Length; y++)
        {
            var src = y - dy;
            copy[y] = src >= 0 && src < frame.Length ? frame[src] : blank;
        }
        return copy;
    }

    /// <summary>같은 높이의 격자들을 가로로 이어 붙인다 (탐사선 불꽃처럼 일부 열만 프레임마다 바뀔 때)</summary>
    private static string[] Beside(params string[][] parts)
    {
        var rows = new string[parts[0].Length];
        for (var y = 0; y < rows.Length; y++) rows[y] = string.Concat(parts.Select(p => p[y]));
        return rows;
    }

    private static void Add(Dictionary<string, PixelSprite> set, string id, double frameSeconds, IReadOnlyDictionary<char, uint> map, params string[][] frames)
    {
        if (set.ContainsKey(id)) throw new InvalidOperationException($"스프라이트 '{id}': id 중복");
        set[id] = Parse(id, frameSeconds, map, frames);
    }

    internal static PixelSprite Parse(string id, double frameSeconds, IReadOnlyDictionary<char, uint> map, string[][] frames)
    {
        if (frames.Length == 0) throw new InvalidOperationException($"스프라이트 '{id}': 프레임 없음");
        if (!(frameSeconds > 0)) throw new InvalidOperationException($"스프라이트 '{id}': 프레임 시간은 0보다 커야 함 ({frameSeconds})");
        var height = frames[0].Length;
        if (height == 0) throw new InvalidOperationException($"스프라이트 '{id}': 빈 프레임");
        var width = frames[0][0].Length;
        if (width == 0) throw new InvalidOperationException($"스프라이트 '{id}': 빈 행");

        var pixels = new uint[frames.Length][];
        for (var f = 0; f < frames.Length; f++)
        {
            var rows = frames[f];
            if (rows.Length != height)
                throw new InvalidOperationException($"스프라이트 '{id}': 프레임 {f} 높이 {rows.Length} ≠ {height}");
            var buf = new uint[width * height];
            for (var y = 0; y < height; y++)
            {
                var row = rows[y];
                if (row.Length != width)
                    throw new InvalidOperationException($"스프라이트 '{id}': 프레임 {f} 행 {y} 너비 {row.Length} ≠ {width}");
                for (var x = 0; x < width; x++)
                {
                    var ch = row[x];
                    if (ch == '.') continue;
                    if (!map.TryGetValue(ch, out var color))
                        throw new InvalidOperationException($"스프라이트 '{id}': 프레임 {f} ({x},{y}) 의 문자 '{ch}' 가 팔레트에 없음");
                    buf[y * width + x] = color;
                }
            }
            pixels[f] = buf;
        }
        return new PixelSprite(id, width, height, frameSeconds, pixels);
    }
}
