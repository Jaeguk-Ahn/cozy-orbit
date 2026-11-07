using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>연구소 소품을 두는 곳</summary>
public enum DecorKind
{
    /// <summary>벽에 거는 소품 (최대 16×12)</summary>
    Wall,
    /// <summary>바닥에 세우는 소품 (최대 18×18, 맨 아래 행이 바닥에 닿는다)</summary>
    Floor,
}

/// <summary>
/// 연구소 소품 하나. 가격 단위는 별사탕. SouvenirOf 가 있으면 기념품 — 그 천체를 발견하면 공짜로 얻는다 (Price 0).
/// 스프라이트 id = decor_&lt;Id&gt;.
/// </summary>
public sealed record DecorDef(string Id, DecorKind Kind, string NameKo, string NameEn, int Price, string? SouvenirOf = null)
{
    public string Name => L.T(NameKo, NameEn);
    public string Sprite => "decor_" + Id;
    public bool IsSouvenir => SouvenirOf != null;
}

/// <summary>
/// 연구소 꾸미기: 별사탕으로 사거나 발견 기념품으로 얻은 소품을 벽·바닥에 놓는다 (종류마다 최대 3개).
/// 산 것은 GameState.OwnedDecor, 기념품은 FoundAt 으로 판단한다. PlacedDecor 의 모르는 id·안 가진 것·중복은 읽을 때 건너뛴다.
/// </summary>
public static class Decor
{
    /// <summary>벽·바닥 각각 놓을 수 있는 소품 수</summary>
    public const int MaxPerKind = 3;

    private static DecorDef Buy(string id, DecorKind kind, string ko, string en, int price) => new(id, kind, ko, en, price);
    private static DecorDef Souvenir(string id, DecorKind kind, string ko, string en, string discoveryId) => new(id, kind, ko, en, 0, discoveryId);

    public static readonly IReadOnlyList<DecorDef> All = new[]
    {
        Buy("plant", DecorKind.Floor, "해바라기 화분", "Sunflower Pot", 30),
        Buy("beanbag", DecorKind.Floor, "빈백 쿠션", "Beanbag", 60),
        Buy("bookshelf", DecorKind.Floor, "작은 책장", "Little Bookshelf", 80),
        Buy("tea_table", DecorKind.Floor, "티 테이블", "Tea Table", 90),
        Buy("wheel", DecorKind.Floor, "쳇바퀴", "Hamster Wheel", 120),
        Buy("telescope", DecorKind.Floor, "망원경", "Telescope", 150),
        Buy("aquarium", DecorKind.Floor, "어항", "Aquarium", 180),
        Buy("star_poster", DecorKind.Wall, "별자리 포스터", "Constellation Poster", 40),
        Buy("shelf_plant", DecorKind.Wall, "벽 선반 화분", "Shelf Plant", 50),
        Buy("clock", DecorKind.Wall, "도토리 시계", "Acorn Clock", 70),
        Buy("string_lights", DecorKind.Wall, "꼬마 전구", "String Lights", 100),

        Souvenir("cheese_cushion", DecorKind.Floor, "치즈 쿠션", "Cheese Cushion", "cheese_moon"),
        Souvenir("satellite_model", DecorKind.Floor, "위성 모형", "Satellite Model", "old_satellite"),
        Souvenir("music_box", DecorKind.Floor, "오르골", "Music Box", "music_box"),
        Souvenir("star_lantern", DecorKind.Wall, "별 등불", "Star Lantern", "lantern_star"),
        Souvenir("golden_acorn", DecorKind.Floor, "황금 도토리 트로피", "Golden Acorn Trophy", "golden_acorn"),
        Souvenir("home_letter", DecorKind.Wall, "고향 편지 액자", "Framed Letter", "home_letter"),
        Souvenir("saturn_lamp", DecorKind.Floor, "토성 램프", "Saturn Lamp", "saturn"),
        Souvenir("moon_globe", DecorKind.Floor, "달 지구본", "Moon Globe", "moon"),
        Souvenir("crab_plush", DecorKind.Floor, "게 인형", "Crab Plush", "crab_nebula"),
        Souvenir("golden_record", DecorKind.Wall, "골든 레코드", "Golden Record", "voyager_1"),
        Souvenir("pleiades_garland", DecorKind.Wall, "일곱 별 가랜드", "Seven-Star Garland", "pleiades"),
        Souvenir("milkomeda_poster", DecorKind.Wall, "밀코메다 포스터", "Milkomeda Poster", "milkomeda"),
        Souvenir("black_hole_photo", DecorKind.Wall, "블랙홀 사진", "Black Hole Photo", "m87_black_hole"),
        Souvenir("deep_field_poster", DecorKind.Wall, "딥 필드 포스터", "Deep Field Poster", "hubble_deep_field"),
    };

    private static readonly Dictionary<string, DecorDef> ById = All.ToDictionary(d => d.Id, StringComparer.Ordinal);
    private static readonly Dictionary<string, DecorDef> BySouvenirOf =
        All.Where(d => d.SouvenirOf != null).ToDictionary(d => d.SouvenirOf!, StringComparer.Ordinal);

    public static DecorDef? Find(string id) => ById.GetValueOrDefault(id);

    public static DecorDef Get(string id) =>
        Find(id) ?? throw new KeyNotFoundException($"소품 없음: {id}");

    /// <summary>이 천체를 발견하면 얻는 기념품 (없으면 null)</summary>
    public static DecorDef? SouvenirFor(string discoveryId) => BySouvenirOf.GetValueOrDefault(discoveryId);

    /// <summary>산 소품이거나, 이어진 천체를 발견한 기념품</summary>
    public static bool Owns(GameState s, string id) => Find(id) is { } d && Owns(s, d);

    private static bool Owns(GameState s, DecorDef d) =>
        d.SouvenirOf != null ? s.FoundAt.ContainsKey(d.SouvenirOf) : s.OwnedDecor.Contains(d.Id);

    /// <summary>기념품은 살 수 없다</summary>
    public static bool CanBuy(GameState s, string id) =>
        Find(id) is { IsSouvenir: false } d && !Owns(s, d) && s.Candy.Balance >= d.Price;

    public static bool TryBuy(GameState s, string id)
    {
        if (!CanBuy(s, id)) return false;
        var d = Get(id);
        s.Candy.Balance -= d.Price;
        s.Stats.CandySpent += d.Price;
        s.OwnedDecor.Add(id);
        return true;
    }

    /// <summary>놓인 소품 (놓은 순서 = 자리 순서). 모르는 id·안 가진 것·중복은 건너뛴다.</summary>
    public static IReadOnlyList<DecorDef> Placed(GameState s, DecorKind kind)
    {
        var list = new List<DecorDef>(MaxPerKind);
        foreach (var id in s.PlacedDecor)
        {
            if (Find(id) is not { } d || d.Kind != kind || !Owns(s, d) || list.Contains(d)) continue;
            list.Add(d);
            if (list.Count == MaxPerKind) break;
        }
        return list;
    }

    public static bool IsPlaced(GameState s, string id) =>
        Find(id) is { } d && Placed(s, d.Kind).Contains(d);

    public static bool CanPlace(GameState s, string id) =>
        Find(id) is { } d && Owns(s, d) && Placed(s, d.Kind) is var placed && placed.Count < MaxPerKind && !placed.Contains(d);

    /// <summary>가진 소품을 빈자리에 놓는다. 같은 종류 자리가 꽉 찼으면 실패.</summary>
    public static bool Place(GameState s, string id)
    {
        if (!CanPlace(s, id)) return false;
        s.PlacedDecor.Remove(id); // 안 가진 채 남아 있던 옛 기록은 지우고 맨 뒤 자리에
        s.PlacedDecor.Add(id);
        return true;
    }

    public static bool Remove(GameState s, string id)
    {
        if (!IsPlaced(s, id)) return false;
        s.PlacedDecor.RemoveAll(x => x == id);
        return true;
    }
}
