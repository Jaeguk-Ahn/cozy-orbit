using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>목적지 안의 구역. Number 는 목적지 안에서 1부터.</summary>
public sealed record SectorDef(int Number, string NameKo, string NameEn)
{
    public string Name => L.T(NameKo, NameEn);
}

/// <summary>
/// 도감 천체 하나 (docs/design.md §6·§11). 스프라이트 id 는 obj_&lt;Id&gt;.
/// Note = 귀여운 한 줄, Fact = 실제 천문 사실 한 줄 (지어낸 천체는 null).
/// </summary>
public sealed record DiscoveryDef(string Id, int Sector, string NameKo, string NameEn, string NoteKo, string NoteEn,
    string? FactKo = null, string? FactEn = null)
{
    public string Name => L.T(NameKo, NameEn);
    public string Note => L.T(NoteKo, NoteEn);
    public string? Fact => FactKo == null || FactEn == null ? null : L.T(FactKo, FactEn);
    public string Sprite => "obj_" + Id;
}

/// <summary>
/// 항해 목적지 (은하·은하단 등). 도감을 다 채우면 다음 목적지로 새 항해를 떠날 수 있다 (Voyages).
/// 발견 거리·보상의 배율은 목적지가 아니라 항해 번호로 정한다 (Balance.VoyageScale) — 수치 조정을 한 곳에서.
/// </summary>
public sealed record DestinationDef(string Id, string NameKo, string NameEn, string NoteKo, string NoteEn,
    IReadOnlyList<SectorDef> Sectors, IReadOnlyList<DiscoveryDef> Catalog)
{
    public string Name => L.T(NameKo, NameEn);
    public string Note => L.T(NoteKo, NoteEn);
    /// <summary>목적지 아이콘 (dest_&lt;Id&gt;, 24×24)</summary>
    public string Sprite => "dest_" + Id;
    /// <summary>정해진 목적지를 다 지난 뒤의 끝없는 우주 (이름 없는 별만)</summary>
    public bool IsEndless => Catalog.Count == 0;
}

/// <summary>목적지 목록과 "이번 항해의 k 번째 발견" 조회.</summary>
public static partial class Destinations
{
    public const string NamelessSprite = "obj_nameless_star";
    /// <summary>끝없는 우주에서 다음 항해를 떠나려면 만나야 하는 이름 없는 별 수</summary>
    public const int EndlessGoal = 12;

    private static readonly Lazy<IReadOnlyList<DestinationDef>> Defined = new(() =>
    {
        // MilkyWay 는 다른 partial 파일의 정적 필드 — Lazy 라 형식 초기화가 끝난 뒤에 읽힌다
        var list = new List<DestinationDef> { MilkyWay! };
        AddDestinations(list);
        return list;
    });

    /// <summary>정해진 목적지 (항해 순서대로). 0 = 우리 은하.</summary>
    public static IReadOnlyList<DestinationDef> All => Defined.Value;

    /// <summary>나머지 목적지는 Destinations.Catalog.cs 에서 순서대로 더한다</summary>
    static partial void AddDestinations(List<DestinationDef> list);

    /// <summary>voyage 번째 항해의 목적지. 정해진 목록을 넘으면 "이름 없는 우주 #n"</summary>
    public static DestinationDef For(int voyage)
    {
        if (voyage >= 0 && voyage < All.Count) return All[voyage];
        int n = voyage - All.Count + 1;
        return new DestinationDef("endless", $"이름 없는 우주 #{n}", $"Nameless Cosmos #{n}",
            "지도에 없는 곳. 이름 없는 별들이 끝없이 반짝여요.", "Off the map. Nameless stars twinkle without end.",
            new[] { new SectorDef(1, "지도 밖", "Off the Map") }, Array.Empty<DiscoveryDef>());
    }

    public static DestinationDef Current(GameState s) => For(s.Voyage);

    /// <summary>이번 항해에서 새 항해를 떠나려면 채워야 하는 발견 수</summary>
    public static int GoalCount(DestinationDef d) => d.IsEndless ? EndlessGoal : d.Catalog.Count;

    /// <summary>k 번째(0부터) 발견 — 도감 밖이면 null (이름 없는 별)</summary>
    public static DiscoveryDef? At(DestinationDef d, int k) => k >= 0 && k < d.Catalog.Count ? d.Catalog[k] : null;

    public static string NameOf(DestinationDef d, int k) =>
        At(d, k)?.Name ?? L.T($"이름 없는 별 #{k - d.Catalog.Count + 1}", $"Nameless Star #{k - d.Catalog.Count + 1}");

    public static string SpriteOf(DestinationDef d, int k) => At(d, k)?.Sprite ?? NamelessSprite;

    /// <summary>k 번째 발견이 속한 구역 (도감 밖이면 마지막 구역)</summary>
    public static SectorDef SectorOf(DestinationDef d, int k) =>
        d.Sectors[(At(d, k)?.Sector ?? d.Sectors.Count) - 1];

    /// <summary>id 로 천체 찾기 (모든 목적지) — 기념품·도감용</summary>
    public static (DestinationDef Destination, DiscoveryDef Discovery)? Find(string id)
    {
        foreach (var d in All)
            foreach (var x in d.Catalog)
                if (x.Id == id) return (d, x);
        return null;
    }
}
