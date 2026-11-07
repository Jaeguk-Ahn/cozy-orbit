using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

public sealed record SectorDef(int Number, string NameKo, string NameEn)
{
    public string Name => L.T(NameKo, NameEn);
}

/// <summary>도감 천체 하나 (docs/design.md §6). 스프라이트 id 는 obj_&lt;Id&gt;.</summary>
public sealed record DiscoveryDef(string Id, int Sector, string NameKo, string NameEn, string NoteKo, string NoteEn)
{
    public string Name => L.T(NameKo, NameEn);
    public string Note => L.T(NoteKo, NoteEn);
    public string Sprite => "obj_" + Id;
}

public static class Discoveries
{
    public const string NamelessSprite = "obj_nameless_star";

    public static readonly IReadOnlyList<SectorDef> Sectors = new[]
    {
        new SectorDef(1, "집 근처 궤도", "Home Orbit"),
        new SectorDef(2, "소행성 초원", "Asteroid Meadow"),
        new SectorDef(3, "방울 거성대", "Bubble Giants"),
        new SectorDef(4, "꿈결 성운", "Dreamy Nebula"),
        new SectorDef(5, "은하 끝", "Galaxy's Edge"),
    };

    /// <summary>만나는 순서대로</summary>
    public static readonly IReadOnlyList<DiscoveryDef> Catalog = new DiscoveryDef[]
    {
        new("pebble_moon", 1, "조약돌 달", "Pebble Moon", "주머니에 쏙 들어갈 것 같은 작은 달.", "A moon small enough to fit in a pocket."),
        new("cotton_cloud", 1, "솜사탕 구름별", "Cotton Candy Cloud", "달콤한 냄새가 나는 것 같아요.", "It seems to smell sweet."),
        new("old_satellite", 1, "낡은 인공위성", "Old Satellite", "아직도 삐삐 신호를 보내고 있어요.", "Still beeping after all these years."),
        new("tiny_comet", 1, "꼬마 혜성", "Tiny Comet", "꼬리를 흔들며 인사해요.", "Wags its tail to say hello."),
        new("bunny_rock", 1, "토끼 바위", "Bunny Rock", "귀가 두 개 달린 소행성.", "An asteroid with two ears."),
        new("star_seed", 1, "별씨앗", "Star Seed", "언젠가 큰 별이 될 거예요.", "Someday it will grow into a big star."),

        new("crystal_rock", 2, "수정 소행성", "Crystal Rock", "햇빛을 받으면 무지개가 생겨요.", "Makes rainbows in the sunlight."),
        new("donut_rock", 2, "도넛 소행성", "Donut Rock", "가운데가 뻥 뚫려 있어요. 먹으면 안 돼요.", "Hollow in the middle. Not for eating."),
        new("cheese_moon", 2, "치즈 달", "Cheese Moon", "햄찌들이 제일 좋아하는 달.", "The hamsters' favorite moon."),
        new("mushroom_rock", 2, "버섯 바위", "Mushroom Rock", "폭신해 보이지만 단단해요.", "Looks fluffy, but it's rock-hard."),
        new("ice_cube", 2, "얼음 조각", "Ice Cube", "천천히 빙글빙글 돌고 있어요.", "Slowly spinning round and round."),
        new("lost_mitten", 2, "잃어버린 장갑", "Lost Mitten", "누가 우주에서 장갑을 떨어뜨렸을까요?", "Who dropped a mitten in space?"),

        new("jelly_giant", 3, "젤리 거성", "Jelly Giant", "말랑말랑 흔들리는 거대한 행성.", "A giant planet that wobbles softly."),
        new("candy_planet", 3, "줄무늬 사탕 행성", "Striped Candy Planet", "빙글빙글 줄무늬가 어지러워요.", "Its swirly stripes make you dizzy."),
        new("teacup_planet", 3, "찻잔 고리 행성", "Teacup Planet", "고리가 찻잔 받침 같아요.", "Its ring looks like a saucer."),
        new("bubble_planet", 3, "비눗방울 행성", "Bubble Planet", "톡 건드리면 터질 것 같아요.", "Looks like it might pop if poked."),
        new("sleepy_storm", 3, "졸린 폭풍눈", "Sleepy Storm", "폭풍도 가끔은 졸려요.", "Even storms get sleepy sometimes."),
        new("twin_berries", 3, "쌍둥이 베리별", "Twin Berries", "늘 붙어 다니는 단짝 행성.", "Best-friend planets that never part."),

        new("pink_nebula", 4, "분홍 성운", "Pink Nebula", "볼이 발그레해지는 색이에요.", "A color that makes your cheeks blush."),
        new("whale_cloud", 4, "고래 구름", "Whale Cloud", "성운 사이를 헤엄치는 큰 고래.", "A big whale swimming through the nebula."),
        new("lantern_star", 4, "등불 별", "Lantern Star", "길 잃은 탐사선을 비춰 줘요.", "Lights the way for lost probes."),
        new("flower_nebula", 4, "꽃 성운", "Flower Nebula", "우주에도 꽃이 피어요.", "Flowers bloom in space, too."),
        new("owl_stars", 4, "부엉이 별자리", "Owl Constellation", "밤새 도토리호를 지켜봐요.", "Watches over the Acorn all night."),
        new("music_box", 4, "오르골 행성", "Music Box Planet", "가만히 들으면 노래가 들려요.", "Listen closely and you'll hear a tune."),

        new("sleepy_blackhole", 5, "꾸벅 블랙홀", "Dozing Black Hole", "자는 동안은 아무것도 안 삼켜요.", "Swallows nothing while it naps."),
        new("golden_acorn", 5, "황금 도토리", "Golden Acorn", "전설로만 듣던 그 도토리!", "The legendary acorn from the old tales!"),
        new("star_cradle", 5, "아기별 요람", "Star Cradle", "아기별들이 새근새근 자고 있어요.", "Baby stars sleeping peacefully."),
        new("rainbow_pulsar", 5, "무지개 펄서", "Rainbow Pulsar", "반짝일 때마다 색이 바뀌어요.", "Changes color with every pulse."),
        new("space_lighthouse", 5, "우주 등대", "Space Lighthouse", "누가 여기 등대를 세웠을까요?", "Who built a lighthouse out here?"),
        new("home_letter", 5, "고향 편지", "Letter from Home", "\"잘 지내니? 도토리 보낸다.\" — 연구소", "\"Doing well? Sending acorns.\" — The Lab"),
    };

    /// <summary>k 번째(0부터) 발견의 이름 (도감 밖이면 "이름 없는 별 #n")</summary>
    public static string NameOf(int k) =>
        k < Catalog.Count ? Catalog[k].Name : L.T($"이름 없는 별 #{k - Catalog.Count + 1}", $"Nameless Star #{k - Catalog.Count + 1}");

    public static string SpriteOf(int k) => k < Catalog.Count ? Catalog[k].Sprite : NamelessSprite;

    /// <summary>k 번째 발견이 속한 구역 (도감 밖이면 마지막 구역)</summary>
    public static SectorDef SectorOf(int k) =>
        Sectors[(k < Catalog.Count ? Catalog[k].Sector : Sectors.Count) - 1];
}
