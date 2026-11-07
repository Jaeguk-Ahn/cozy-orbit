namespace CozyOrbit.Core.Art;

// UI 아이콘 (12×12, 한 프레임)
public static partial class SpriteLibrary
{
    private static void AddIcons(Dictionary<string, PixelSprite> set)
    {
        // 별빛 (에너지)
        Add(set, "icon_starlight", 1.0, Base,
        [
            ".....k......",
            "....kyk.....",
            "....kyk.....",
            "...kywyk....",
            ".kkyywyykk..",
            "kyyywwwyyyk.",
            ".kkyywyykk..",
            "...kyyyk....",
            "....kyk..k..",
            "....kyk.kyk.",
            ".....k...k..",
            "............",
        ]);

        // 별가루 (화폐)
        Add(set, "icon_stardust", 1.0, Base,
        [
            "...k........",
            "..kvk.......",
            ".kvwvk......",
            "kvwwwvk.....",
            ".kvwvk..k...",
            "..kvk..kvk..",
            "...k..kvwvk.",
            ".......kvk..",
            "..k.....k...",
            ".klk........",
            "..k......k..",
            ".........l..",
        ]);

        // 거리 (별걸음) — 이정표 깃발
        Add(set, "icon_distance", 1.0, Base,
        [
            "...k........",
            "...kkkkk....",
            "...kRRRRkk..",
            "...kRwRRRRk.",
            "...kRRRRkk..",
            "...kkkkk....",
            "...k........",
            "...k........",
            "...k........",
            "..kjk.......",
            ".kiiik.l.l.l",
            "............",
        ]);

        // 도토리 엔진
        Add(set, "icon_engine", 1.0, Base,
        [
            ".....kk.....",
            "...kkddkk...",
            "..kbbdbbbk..",
            ".kbdbbbdbbk.",
            ".kkkkkkkkkk.",
            "..kcttttbk..",
            "..kcttttbk..",
            "..kttttbbk..",
            "...kttbbk...",
            "....kkkk....",
            "....ayya....",
            ".....aa.....",
        ]);

        // 별빛 배터리
        Add(set, "icon_battery", 1.0, Base,
        [
            "............",
            "............",
            "kkkkkkkkkkk.",
            "knnnnnnnnnk.",
            "knwynyynjjkk",
            "knyynyynjjkk",
            "knyynyynjjkk",
            "knyynyynjjkk",
            "knnnnnnnnnk.",
            "kkkkkkkkkkk.",
            "............",
            "............",
        ]);

        // 태양 돛
        Add(set, "icon_sail", 1.0, Base,
        [
            ".y...k......",
            "yyy..kk.....",
            ".y...kwk....",
            "....kkssk...",
            "...kwkwssk..",
            "..kwwksssek.",
            ".kwhhkwsssek",
            "kkkkkkkkkkkk",
            "kttttttttttk",
            ".kbbbbbbbbk.",
            "..kkkkkkkk..",
            "............",
        ]);

        // 반짝 부스터 — 번개
        Add(set, "icon_booster", 1.0, Base,
        [
            "......kkkk..",
            ".....kwyyk..",
            "....kwyyk...",
            "...kwyyk....",
            "..kwyyykkk..",
            ".kyyyyyyak..",
            ".kkkkyyak...",
            "....kyak....",
            "...kyak.....",
            "...kak......",
            "..kak.......",
            "..kk........",
        ]);

        // 별가루 망원경
        Add(set, "icon_scope", 1.0, Base,
        [
            ".........kk.",
            "........kwsk",
            ".......kssek",
            "......kyssk.",
            ".....kyyek..",
            "....kesek...",
            "...kesek....",
            "..kesek.....",
            ".kkek.k.....",
            ".kk..kjk....",
            "....k.k.k...",
            "...k..k..k..",
        ]);

        // 햄찌 크루
        Add(set, "icon_crew", 1.0, Base,
        [
            ".kk......kk.",
            "kppk....kppk",
            "kpgkkkkkkgpk",
            ".kggggggggk.",
            "kggggggggggk",
            "kgwkggggwkgk",
            "kgkkgccgkkgk",
            "kppgcrrcgppk",
            "kgggcKKcgggk",
            ".kgggccgggk.",
            "..kkkkkkkk..",
            "............",
        ]);

        // 포근한 침낭
        Add(set, "icon_bunk", 1.0, Base,
        [
            "........llll",
            "...........l",
            "..........l.",
            "..kkkkk..l..",
            ".kgggggk.lll",
            ".kKgggKk....",
            "kkpgrgpkkkk.",
            "kllllllllllk",
            "klwlllvllllk",
            "kllllllvlllk",
            ".kuuuuuuuuk.",
            "..kkkkkkkk..",
        ]);

        // 연구
        Add(set, "icon_lab", 1.0, Base,
        [
            "...kkkkkk...",
            "...kkkkkk...",
            "....kwhk....",
            "....kwhk....",
            "....khhk....",
            "...khhhhk...",
            "...khhhhk...",
            "..kTTTTTTk..",
            "..kmmwmmmk..",
            ".kmwmmmwmmk.",
            ".kkkkkkkkkk.",
            "............",
        ]);

        // 도감
        Add(set, "icon_book", 1.0, Base,
        [
            "kkkkkkkkkk..",
            "kuKuuuuuukkk",
            "kuKuuuuuukwk",
            "kuKuuuyuukwk",
            "kuKuuyyyukwk",
            "kuKuyywyykwk",
            "kuKuuyyyukwk",
            "kuKuuyuyukwk",
            "kuKuuuuuukwk",
            "kuKuuuuuukwk",
            "kkkkkkkkkkwk",
            "..kkkkkkkkkk",
        ]);

        // 항해 일지
        Add(set, "icon_log", 1.0, Base,
        [
            ".kkkkkkkkkk.",
            ".kkkkkkkkkk.",
            "..kcccccck..",
            "..kciiiick..",
            "..kcccccck..",
            "..kciiiick..",
            "..kcccccck..",
            "..kciiiick..",
            "..kcccccck..",
            "..kciiccck..",
            ".kkkkkkkkkk.",
            ".kkkkkkkkkk.",
        ]);

        // 설정
        Add(set, "icon_gear", 1.0, Base,
        [
            "....kkkk....",
            ".kk.kwhk.kk.",
            ".kwkkhhkkhk.",
            ".kkhhhhhhkk.",
            "kkkhhkkhhkkk",
            "kwhhk..khhik",
            "khhhk..khiik",
            "kkkhhkkhikkk",
            ".kkhhhhiikk.",
            ".khkkhiikik.",
            ".kk.kiik.kk.",
            "....kkkk....",
        ]);

        // 알 수 없음 (대체 아이콘)
        Add(set, "icon_unknown", 1.0, Base,
        [
            "...kkkkkk...",
            "..kwlllllk..",
            ".kwllkkkllk.",
            "kwllkklkkllk",
            "kllllllkkllk",
            "klllllkklllk",
            "kllllkkllllk",
            "kllllllllllk",
            "kllllkkllluk",
            ".kllllllluk.",
            "..kllllluk..",
            "...kkkkkk...",
        ]);

        // 상점 — 줄무늬 차양 가게
        Add(set, "icon_shop", 1.0, Base,
        [
            "............",
            ".kkkkkkkkkk.",
            "kwwrrwwrrwwk",
            "kwwrrwwrrwwk",
            "kkkkkkkkkkkk",
            ".kcccccccck.",
            ".kbbcKKKKck.",
            ".kbbcKspKck.",
            ".kbycKppKck.",
            ".kbbcKKKKck.",
            ".kbbcccccck.",
            "kkkkkkkkkkkk",
        ]);

        // 별사탕 (상점 화폐) — 오톨도톨 분홍 별사탕
        Add(set, "icon_candy", 1.0, Base,
        [
            "...kk..kk...",
            "..kppkkppk..",
            ".kkpwwpppkk.",
            "kpwwpppppprk",
            "kpwppppppprk",
            ".kppppppprk.",
            ".kppppppprk.",
            "kppppppprrrk",
            "kppppppprrrk",
            ".kkpppprrkk.",
            "..kprkkrrk..",
            "...kk..kk...",
        ]);
    }
}
