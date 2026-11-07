using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>스킨을 끼우는 자리. 자리마다 하나만 장착한다.</summary>
public enum SkinSlot
{
    /// <summary>선장 햄찌 복장 (햄찌 위에 겹쳐 그리는 스프라이트)</summary>
    Outfit,
    /// <summary>선장 햄찌 털색</summary>
    Fur,
    /// <summary>탐사선 생김새</summary>
    Ship,
    /// <summary>장면 배경 테마</summary>
    Theme,
}

/// <summary>
/// 상점 스킨 하나 (docs/design.md §9). 가격 단위는 별사탕.
/// 스프라이트 규칙: Outfit = Sprite 를 햄찌 위에 겹침(null = 맨몸), Fur = 햄찌 스프라이트, Ship = 평소/부스트 스프라이트, Theme = SceneThemes id.
/// </summary>
public sealed record SkinDef
{
    public required string Id { get; init; }
    public required SkinSlot Slot { get; init; }
    public required string NameKo { get; init; }
    public required string NameEn { get; init; }
    public required string NoteKo { get; init; }
    public required string NoteEn { get; init; }
    public required int Price { get; init; }
    public string? Sprite { get; init; }
    public string? BoostSprite { get; init; }
    public string? ThemeId { get; init; }

    public string Name => L.T(NameKo, NameEn);
    public string Note => L.T(NoteKo, NoteEn);
    public bool IsFree => Price == 0;
}

public static class Skins
{
    /// <summary>복장 겹치기 스프라이트의 캔버스: 20×22, 햄찌(16×16)의 왼쪽 위가 (2, 6) 에 온다 — 모자가 머리 위로 나갈 자리</summary>
    public const int OutfitWidth = 20;
    public const int OutfitHeight = 22;
    public const int OutfitOriginX = 2;
    public const int OutfitOriginY = 6;

    public const string DefaultOutfit = "outfit_none";
    public const string DefaultFur = "fur_gold";
    public const string DefaultShip = "ship_acorn";
    public const string DefaultTheme = "theme_space";

    private static SkinDef Outfit(string id, string ko, string en, string noteKo, string noteEn, int price) => new()
    {
        Id = id, Slot = SkinSlot.Outfit, NameKo = ko, NameEn = en, NoteKo = noteKo, NoteEn = noteEn, Price = price,
        Sprite = id == DefaultOutfit ? null : id,
    };

    private static SkinDef Fur(string id, int index, string ko, string en, int price) => new()
    {
        Id = id, Slot = SkinSlot.Fur, NameKo = ko, NameEn = en, NoteKo = "선장 햄찌의 털색", NoteEn = "The captain's fur color", Price = price,
        Sprite = "hamster_" + index,
    };

    private static SkinDef Ship(string id, string ko, string en, string noteKo, string noteEn, int price) => new()
    {
        Id = id, Slot = SkinSlot.Ship, NameKo = ko, NameEn = en, NoteKo = noteKo, NoteEn = noteEn, Price = price,
        Sprite = id == DefaultShip ? "probe" : id,
        BoostSprite = id == DefaultShip ? "probe_boost" : id + "_boost",
    };

    private static SkinDef Theme(string id, string ko, string en, string noteKo, string noteEn, int price) => new()
    {
        Id = id, Slot = SkinSlot.Theme, NameKo = ko, NameEn = en, NoteKo = noteKo, NoteEn = noteEn, Price = price,
        ThemeId = id["theme_".Length..],
    };

    /// <summary>상점 표시 순서 (자리별로 묶고, 자리 안에서는 가격순)</summary>
    public static readonly IReadOnlyList<SkinDef> All = new[]
    {
        Outfit(DefaultOutfit, "맨몸", "No outfit", "보송보송 그대로가 제일 귀여워요.", "Fluffy as is — the cutest.", 0),
        Outfit("outfit_scarf", "포근 목도리", "Cozy Scarf", "우주는 생각보다 추워요.", "Space is colder than you'd think.", 30),
        Outfit("outfit_labcoat", "연구 가운", "Lab Coat", "고글까지 쓰면 진짜 연구원.", "With goggles, a real researcher.", 80),
        Outfit("outfit_pajama", "꿀잠 잠옷", "Sleepy Pajamas", "자리 비움 중에도 푹 자요.", "Sleeps tight while you're away.", 100),
        Outfit("outfit_helmet", "우주 헬멧", "Space Helmet", "동그란 유리 헬멧과 안테나.", "A round glass helmet with an antenna.", 150),
        Outfit("outfit_pirate", "우주 해적 선장", "Space Pirate", "보물은 해바라기씨예요.", "The treasure is sunflower seeds.", 200),
        Outfit("outfit_wizard", "별 마법사", "Star Wizard", "모자에서 별이 반짝여요.", "Stars twinkle on the hat.", 300),
        Outfit("outfit_king", "꼬마 왕", "Little King", "왕관과 빨간 망토.", "A crown and a red cape.", 500),

        Fur(DefaultFur, 0, "황금 털", "Golden", 0),
        Fur("fur_cream", 1, "크림 털", "Cream", 20),
        Fur("fur_gray", 2, "회색 털", "Gray", 20),
        Fur("fur_choco", 3, "초코 털", "Chocolate", 20),
        Fur("fur_white", 4, "눈송이 털", "Snowflake", 20),
        Fur("fur_apricot", 5, "살구 털", "Apricot", 20),

        Ship(DefaultShip, "도토리호", "The Acorn", "연구소가 처음 만든 탐사선.", "The lab's very first probe.", 0),
        Ship("ship_golden_acorn", "황금 도토리호", "Golden Acorn", "반짝반짝 금박을 입혔어요.", "Gilded and gleaming.", 120),
        Ship("ship_strawberry", "딸기호", "Strawberry", "씨앗 무늬 창문이 포인트.", "Seed-dotted windows.", 200),
        Ship("ship_retro", "레트로 로켓", "Retro Rocket", "옛날 그림책 속 로켓.", "Straight out of an old picture book.", 250),
        Ship("ship_whale", "고래호", "Whale", "물 대신 별을 뿜어요.", "Spouts stars instead of water.", 400),
        Ship("ship_teapot", "찻주전자호", "Teapot", "김 대신 불꽃이 나와요.", "Flames instead of steam.", 400),
        Ship("ship_ufo", "햄찌 UFO", "Hamster UFO", "유리 돔 안에서 손을 흔들어요.", "Waving from inside the glass dome.", 600),

        Theme(DefaultTheme, "기본 우주", "Deep Space", "남보라빛 고요한 우주.", "A calm indigo sky.", 0),
        Theme("theme_sunset", "노을 우주", "Sunset Space", "복숭아빛 노을 속을 날아요.", "Flying through a peach sunset.", 150),
        Theme("theme_aurora", "민트 오로라", "Mint Aurora", "초록빛 오로라가 일렁여요.", "Green auroras ripple by.", 250),
        Theme("theme_strawberry_milk", "딸기 우유", "Strawberry Milk", "온통 분홍분홍.", "Pink, pink, everywhere.", 250),
        Theme("theme_deep_sea", "우주 바다", "Space Sea", "해파리가 둥실둥실 떠다녀요.", "Jellyfish drift along.", 400),
        Theme("theme_candy", "사탕 은하", "Candy Galaxy", "달콤한 사탕별 사이로.", "Through sweet candy stars.", 600),
    };

    public static SkinDef Get(string id) => All.First(s => s.Id == id);

    public static SkinDef? Find(string? id) => id == null ? null : All.FirstOrDefault(s => s.Id == id);

    public static string DefaultFor(SkinSlot slot) => slot switch
    {
        SkinSlot.Outfit => DefaultOutfit,
        SkinSlot.Fur => DefaultFur,
        SkinSlot.Ship => DefaultShip,
        _ => DefaultTheme,
    };

    public static string SlotName(SkinSlot slot) => slot switch
    {
        SkinSlot.Outfit => L.T("선장 복장", "Captain's Outfit"),
        SkinSlot.Fur => L.T("선장 털색", "Captain's Fur"),
        SkinSlot.Ship => L.T("탐사선", "Ship"),
        _ => L.T("배경 테마", "Background Theme"),
    };
}
