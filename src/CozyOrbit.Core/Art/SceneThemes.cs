namespace CozyOrbit.Core.Art;

/// <summary>
/// 배경 장식 하나. X·Y 는 0~1 비율 (X = 장면 너비 기준, 스크롤하면 감싼다 / Y = 우주 영역 높이 기준, 스프라이트 위쪽).
/// Parallax 는 별 스크롤 대비 속도 (0 = 고정, 0.05 = 아주 먼 배경, 1 = 가까운 별과 같은 속도).
/// </summary>
public sealed record ThemeDecoration(string Sprite, double X, double Y, double Parallax);

/// <summary>
/// 장면(우주 + 연구소 단면) 색과 장식. UI 패널 색은 테마와 상관없이 Palette 의 UI 색을 쓴다.
/// </summary>
public sealed record SceneTheme
{
    public required string Id { get; init; }
    /// <summary>하늘 그라데이션 위·아래</summary>
    public required uint SkyTop { get; init; }
    public required uint SkyBottom { get; init; }
    /// <summary>가장 먼 별 점 색</summary>
    public required uint StarDot { get; init; }
    /// <summary>중간·가까운 별 스프라이트</summary>
    public string StarSmall { get; init; } = "star_small";
    public string StarBig { get; init; } = "star_big";
    // 연구소 단면
    public required uint Wall { get; init; }
    public required uint WallDark { get; init; }
    public required uint Line { get; init; }
    public required uint Window { get; init; }
    public required uint Lamp { get; init; }
    public required uint Floor { get; init; }
    public required uint FloorLight { get; init; }
    public required uint FloorDark { get; init; }
    public IReadOnlyList<ThemeDecoration> Decorations { get; init; } = Array.Empty<ThemeDecoration>();
}

/// <summary>배경 테마 목록. id 는 Game/Skins.cs 의 테마 스킨과 같다.</summary>
public static partial class SceneThemes
{
    public const string DefaultId = "space";

    /// <summary>기본 우주 — 처음 게임의 색 그대로</summary>
    public static readonly SceneTheme Space = new()
    {
        Id = DefaultId,
        SkyTop = Palette.SpaceDeep,
        SkyBottom = Palette.Space,
        StarDot = Palette.TextDim,
        Wall = Palette.PanelLight,
        WallDark = Palette.Panel,
        Line = Palette.Outline,
        Window = Palette.SpaceDeep,
        Lamp = Palette.Butter,
        Floor = Palette.Brown,
        FloorLight = Palette.Tan,
        FloorDark = Palette.BrownDark,
    };

    private static readonly Lazy<Dictionary<string, SceneTheme>> ById = new(() =>
    {
        var list = new List<SceneTheme> { Space };
        AddThemes(list);
        return list.ToDictionary(t => t.Id, StringComparer.Ordinal);
    });

    public static IReadOnlyCollection<SceneTheme> All => ById.Value.Values;

    /// <summary>모르는 id 는 기본 우주</summary>
    public static SceneTheme Get(string? id) => id != null && ById.Value.TryGetValue(id, out var t) ? t : Space;

    /// <summary>테마 추가는 SceneThemes.Catalog.cs 에서</summary>
    static partial void AddThemes(List<SceneTheme> list);
}
