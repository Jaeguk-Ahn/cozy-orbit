namespace CozyOrbit.Core.Art;

// 배경 테마 장식 (deco_*) 과 테마별 별 스프라이트. 테마 색·배치는 SceneThemes.Catalog.cs
public static partial class SpriteLibrary
{
    private static void AddThemes(Dictionary<string, PixelSprite> set)
    {
        // 장식은 외곽선 없이 한 톤 낮은 색으로 — 외곽선 있는 탐사선·발견 천체가 앞에 또렷이 보이게
        var sunMap = With(
            ('1', Palette.SunsetSun1), ('2', Palette.SunsetSun2), ('3', Palette.SunsetSun3), ('4', Palette.SunsetSun4), ('5', Palette.SunsetSun5));
        var cloudMap = With(
            ('L', Palette.SunsetCloudLight), ('C', Palette.SunsetCloud), ('D', Palette.SunsetCloudShade), ('E', Palette.SunsetSun5));
        var ringPlanetMap = With(
            ('P', Palette.SunsetPlanet), ('Q', Palette.SunsetPlanetShade), ('H', Palette.SunsetPlanetLight), ('Z', Palette.SunsetRing), ('z', Palette.SunsetRingBack));
        var auroraMap = With(
            ('A', Palette.AuroraEdge), ('B', Palette.AuroraGreen), ('C', Palette.AuroraTeal), ('D', Palette.AuroraHaze), ('E', Palette.AuroraFade));
        var snowMoonMap = With(
            ('I', Palette.AuroraMoon), ('J', Palette.AuroraMoonShade), ('O', Palette.AuroraMoonCrater));
        var strawberryMap = With(
            ('R', Palette.MilkStrawberry), ('S', Palette.MilkStrawberryShade), ('L', Palette.Pink));
        var cartonMap = With(
            ('h', Palette.MilkCartonShade), ('R', Palette.MilkStrawberry));
        var jellyMap = With(
            ('F', Palette.SeaJelly), ('G', Palette.SeaJellyShade));
        var whaleMap = With(
            ('W', Palette.SeaWhale), ('X', Palette.SeaWhaleBack), ('V', Palette.SeaWhaleBelly), ('K', Palette.SeaSkyTop));
        var coralMap = With(
            ('O', Palette.SeaCoral), ('U', Palette.SeaRock), ('M', Palette.SeaWeed));
        var bubbleMap = With(('B', Palette.SeaBubble));
        var lollipopMap = With(('P', Palette.CandyWhiteShade));
        var wrappedCandyMap = With(('h', Palette.CandyWhiteShade));

        // ── 노을 우주 ──
        // 지는 해 — 지평선에 반쯤 걸친 졸린 얼굴, 아래쪽은 옛날 그림책처럼 줄무늬로 끊긴다
        Add(set, "deco_sunset_sun", 1.0, sunMap,
        [
            "................22222222................",
            "............2222111111112222............",
            "..........222ww111111111111222..........",
            ".........22ww111111111111111122.........",
            ".......221w1111111111111111111122.......",
            "......2211111111111111111111111122......",
            ".....221111111111111111111111111122.....",
            "....33222222222222222222222222222233....",
            "....3222222222K2K222222K2K2222222223....",
            "...322222222222K22222222K222222222223...",
            "..3322222222pp22222KK22222pp2222222233..",
            "..322222222222222222222222222222222223..",
            ".44333333333333333333333333333333333344.",
            "........................................",
            ".43333333333333333333333333333333333334.",
            ".43333333333333333333333333333333333334.",
            "........................................",
            "5444444444444444444444444444444444444445",
            "........................................",
            "5444444444444444444444444444444444444445",
        ]);
        // 분홍 구름 (평평한 밑면)
        Add(set, "deco_pink_cloud", 1.0, cloudMap,
        [
            "...........CLLC.................",
            ".........CLLCCCCC...CLLC........",
            "........CCCCCCCCCCCCCCCCCC......",
            "...CLLC.CCCCCCCCCCCCCCCCCCC.....",
            "..CCCCCCCCCCCCCCCCCCCCCCCCCC....",
            ".CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC.",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
            "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
            ".EEEEEEEEEEEEEEEEEEEEEEEEEEEEEE.",
            "...EEEEEEEEEEEEEEEEEEEEEEEEEE...",
        ]);
        // 고리 행성 — 라일락 몸에 복숭아빛 고리
        Add(set, "deco_sunset_planet", 1.0, ringPlanetMap,
        [
            "..........PPPP..........",
            "........PPPHHPPP........",
            ".......PPHHHPPPPP.......",
            ".......PHHHPPPPPP.......",
            ".....zPPHHPPPPPPPPz.....",
            ".Zzzz.PHHPPPPPPPPQ.zzzZ.",
            "ZZ....PHPPPPPPPPQQ....ZZ",
            "ZZ....PPPPPPPPQQQQ....ZZ",
            ".ZZZZ..PPPPPQQQQQ..ZZZZ.",
            ".....ZZZZZZZZZZZZZZ.....",
            "........PQQQQQQQ........",
            "..........QQQQ..........",
        ]);

        // ── 민트 오로라 ──
        // 오로라 커튼 (반투명, 일렁임)
        Add(set, "deco_aurora", 0.8, auroraMap,
        [
            "..............................EEEDE.E...........",
            "..............................DEEDEEE...........",
            "...........................E.EDDDDEEE...........",
            "..................E........EEEDDDDDDD...........",
            "..................E........EEEDDDCDDDE.E........",
            ".................EEE.E.....DDDCDDCDDDEEE..E.....",
            "...............EEEEEEE..E.EDDDCCCCCCCDDD..E.....",
            "...............EEEDEEEE.EEEDDDCCCCCCCDDDEED.....",
            "...............EEDDDEDEEDEDCCCCCCBCCCCCCDDD.....",
            "......E.....E.EDDDDDDDDEDDDCCCBBBBBBBCCCDDCEEE..",
            "......E..E..EEEDDDDDDDDDDDCCCCBBBBBBBBBBCCCDDD..",
            "...E.EDEEE..DEDDDDCCDCDDCCCBBBBBBAAAAAAABBBCCC..",
            "...DEDDEEDEEDDDDCCCCCCCCCCBBBBAAA.......AAABBB..",
            "...DDDDDDDEEDDDCCCCCCCCCBBBAAA.............AAA..",
            "..DCDCCDDDDDCDCCCCCBCBBBBAA.....................",
            "..CCCCCCCCDDCCCCBBBBBBBBA.......................",
            "..ABBBBCCCCCCCCBBBBBBBAA........................",
            "...AAABBBBCCBBBBBBBAAA..........................",
            "......AAABBBBBBBAAA.............................",
            ".........AAAAAAA................................",
        ],
        [
            "...........................EEEDEEE..............",
            "...........................EEDDEEE..............",
            "..........................EDEDDDEDE.E...........",
            "...............E..E.....E.EDDDDDDDEEE..E........",
            "...............EE.E.....EEDDDDCDDDDED..E........",
            "...............EEEE..E..DEDDDCCCDCDDDE.D........",
            "..............EEEEDEEE..DDDCCCCCCCDDDEEDEEE.....",
            "............EEEDDEDEEDEEDDCCCCCCCCCCCDDDEEE.....",
            "............EEEDDDDDDDEECDCCCBBBCBCCCDDCDDD.....",
            "............EEDDDDDDDDDDCCCBBBBBBBBBBCCCDDDE....",
            "...........EDDDDDDCDDCDDCCBBBBBBBBBBBCCBCCCD....",
            ".........E.EDDDCCCCCCCCCBBBBBAAAAAAAABBBCCCDEE..",
            "...E..E..EEDDDCCCCCCCCCCBBAAA........AAABBBCDD..",
            "...D..E..DEDCCCCCCBCBBBBAA..............AABBCC..",
            "...DEEDEEDDDCCCCBBBBBBAA..................AABB..",
            "...CDDDDDCDCCCBBBBBBAA......................AA..",
            "..DCCDCDDCCCBBBBBBAA............................",
            "..CBBCCCCBCBBBBBAA..............................",
            "..AAABBBBBBBBAAA................................",
            ".....AAAAAAAA...................................",
        ]);
        // 눈빛 달
        Add(set, "deco_snow_moon", 1.0, snowMoonMap,
        [
            "....IIIIIIII....",
            "...IIwIIIIIII...",
            "..IwwIIIIIIIII..",
            ".IIwIIIIIOOIIII.",
            ".IwIIIIIIIOIIII.",
            ".IIIIIIIIIIIIII.",
            ".IIIIIIOIIIIIIJ.",
            ".IIIIIIIIIIIIJJ.",
            ".IIIOOIIIIIIJJJ.",
            ".IIIIOIIIIIJJJJ.",
            ".IIIIIIIIIOOJJJ.",
            "..IIIIIIIJJJJJ..",
            "...IIIIIJJJJJ...",
            "....IIIJJJJJ....",
        ]);

        // ── 딸기 우유 ──
        // 둥실 딸기
        Add(set, "deco_strawberry", 0.9, strawberryMap,
        [
            ".......TT.......",
            "...mm.mTTm.mm...",
            "..SmmmmTTmmmmS..",
            ".SRRRmmRRmmRRRS.",
            "SRLRRRRRRRRyRRRS",
            "SLRRyRRRRRRRRRRS",
            "SRRRRRRRyRRRRyRS",
            "SRyRRRRRRRRRRRRS",
            ".SRRRRyRRRRyRRS.",
            ".SRRRRRRRRRRRRS.",
            "..SRyRRRRRyRRS..",
            "..SRRRRRRRRRRS..",
            "...SRRRyRRRRS...",
            "....SRRRRRRS....",
            ".....SRRRRS.....",
            "......SSSS......",
            "................",
        ],
        [
            "................",
            ".......TT.......",
            "...mm.mTTm.mm...",
            "..SmmmmTTmmmmS..",
            ".SRRRmmRRmmRRRS.",
            "SRLRRRRRRRRyRRRS",
            "SLRRyRRRRRRRRRRS",
            "SRRRRRRRyRRRRyRS",
            "SRyRRRRRRRRRRRRS",
            ".SRRRRyRRRRyRRS.",
            ".SRRRRRRRRRRRRS.",
            "..SRyRRRRRyRRS..",
            "..SRRRRRRRRRRS..",
            "...SRRRyRRRRS...",
            "....SRRRRRRS....",
            ".....SRRRRS.....",
            "......SSSS......",
        ]);
        // 콩닥 하트
        Add(set, "deco_heart", 0.5, Base,
        [
            "..pppr....pppr..",
            ".pwwppr..pppppr.",
            "pwpppppppppppppr",
            "pppppppppppppppr",
            "rppppppppppppppr",
            ".rppppppppppppr.",
            "..rppppppppppr..",
            "...rppppppppr...",
            "....rppppppr....",
            ".....rppppr.....",
            "......rppr......",
            ".......rr.......",
        ],
        [
            "................",
            "...ppr....ppr...",
            "..pwwpr..ppppr..",
            "..pppppppppppr..",
            "..rppppppppppr..",
            "...rppppppppr...",
            "....rppppppr....",
            ".....rppppr.....",
            "......rppr......",
            ".......rr.......",
            "................",
            "................",
        ]);
        // 딸기 우유 팩
        Add(set, "deco_milk_carton", 1.0, cartonMap,
        [
            "....wwwwwwww....",
            "....hhhhhhhh....",
            "...wwwwwwwwwh...",
            "..wwwwwwwwwwwh..",
            ".wwwwwwwwwwwwwh.",
            "pppppppppppppprr",
            "pppppppppppppprr",
            "pwwwwwwwwwwwwprr",
            "pwwwwwTmTwwwwprr",
            "pwwwwRRTRRwwwprr",
            "pwwwRRyRRyRwwprr",
            "pwwwRRRRRRRwwprr",
            "pwwwwRyRRRwwwprr",
            "pwwwwwRRRwwwwprr",
            "pwwwwwwRwwwwwprr",
            "pwwwwwwwwwwwwprr",
            "pppppppppppppprr",
            "pppppppppppppprr",
            "pppppppppppppprr",
            "rrrrrrrrrrrrrrrr",
        ]);
        // 우유빛 작은 별 (반짝)
        Add(set, "star_milk_small", 0.7, Base,
        [
            ".w.",
            "wpw",
            ".w.",
        ],
        [
            "...",
            ".w.",
            "...",
        ]);
        // 우유빛 큰 별 (반짝)
        Add(set, "star_milk_big", 0.6, Base,
        [
            "..w..",
            ".www.",
            "wwpww",
            ".www.",
            "..w..",
        ],
        [
            "..w..",
            "..w..",
            "wwpww",
            "..w..",
            "..w..",
        ]);

        // ── 우주 바다 ──
        // 해파리 (다리 살랑)
        Add(set, "deco_jellyfish", 0.6, jellyMap,
        [
            "......FFFF......",
            "....FFFFFFFF....",
            "...FwwFFFFFFG...",
            "..FwwFFFFFFFFG..",
            "..FwFFFFFFFFFG..",
            ".FFFFKFFFFKFFFG.",
            ".FFFpFFFFFFpFFG.",
            ".FFFFFFFFFFFFFG.",
            ".GFGFGFGFGFGFGG.",
            "...G..G..G..G...",
            "...G...G.G...G..",
            "....G..G..G..G..",
            "....G.G...G.G...",
            "...G..G..G..G...",
            "...l...G.G...l..",
            "....l..G..G..l..",
            "......G...G.....",
            "......l..l......",
            ".......l.l......",
            "................",
        ],
        [
            "......FFFF......",
            "....FFFFFFFF....",
            "...FwwFFFFFFG...",
            "..FwwFFFFFFFFG..",
            "..FwFFFFFFFFFG..",
            ".FFFFKFFFFKFFFG.",
            ".FFFpFFFFFFpFFG.",
            ".FFFFFFFFFFFFFG.",
            ".GFGFGFGFGFGFGG.",
            "....G..G..G..G..",
            "....G.G...G.G...",
            "...G..G..G..G...",
            "...G...G.G...G..",
            "....G..G..G..G..",
            "....l.G...G.l...",
            "...l..G..G..l...",
            ".......G.G......",
            ".......l..l.....",
            "......l...l.....",
            "................",
        ]);
        // 먼 고래 그림자
        Add(set, "deco_space_whale", 1.0, whaleMap,
        [
            "...............................WW....WW.",
            "................................WW..WW..",
            ".................................WWWW...",
            ".......XXXXXXXXXXXXXXXX..........WWW....",
            ".....XXWWWWWWWWWWWWWWWWXX......XWWW.....",
            "...XXWWWWWWWWWWWWWWWWWWWWXXXXXWWWW......",
            "..XWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW.......",
            ".XWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW........",
            ".WWWWWKWWWWWWWWWWWWWWWWWWWWWWWW.........",
            ".WWWWWWWWWWWWWWWWWWWWWWWWWWWWW..........",
            "..WWWWWWWWWWWWWWWWWWWWWWWWWWW...........",
            "...VVVVVVVVVVVVVVVVVVVVVVVWW............",
            ".....WWWWWWWWWWWWWWWWWWWW...............",
            ".......VVVVVVVVVVVVVVVV.................",
        ]);
        // 산호와 해초 (우주 바닷속 바닥)
        Add(set, "deco_coral", 1.0, coralMap,
        [
            "..O...O.................O..O..",
            "..O..OO.........M.......O.OO..",
            "..OO.O....O......M.....OOO....",
            "...OOO...OO.....MM......O..O..",
            "....O...O..O....M......OOOO...",
            "....O..O...O...MM........O....",
            "....OOOO..OO...M.....O...O....",
            ".....O...O.....MM....OO.OO....",
            ".....OOOOO......M.....OOO.....",
            "......OO........MM.....O......",
            "......OO.........M.....O......",
            ".....UOOU........M....UOU.....",
            "....UUUUUU......UMU..UUUUU....",
            "..UUUUUUUUUUUUUUUUUUUUUUUUUU..",
        ]);
        // 작은 거품 (반짝)
        Add(set, "star_bubble_small", 0.8, bubbleMap,
        [
            ".B.",
            "B.B",
            ".B.",
        ],
        [
            ".w.",
            "B.B",
            ".B.",
        ]);
        // 큰 거품 (둥실)
        Add(set, "star_bubble_big", 0.7, bubbleMap,
        [
            ".BBB.",
            "Bw..B",
            "B...B",
            "B...B",
            ".BBB.",
        ],
        [
            ".....",
            ".BBB.",
            "Bw..B",
            "B...B",
            ".BBB.",
        ]);

        // ── 사탕 은하 ──
        // 소용돌이 막대사탕
        Add(set, "deco_lollipop", 1.0, lollipopMap,
        [
            ".....rrrrrrrr.....",
            "....rppppppppr....",
            "...rpwwwwwwpppr...",
            "..rpwwwwwwwwwppr..",
            ".rpwwwwpppwwwpppP.",
            ".rwwwppppppwwwppP.",
            ".rwwppppppppwwppr.",
            ".rwwpppwwpppwwppr.",
            ".PwwppwwwppwwwppP.",
            ".PwwppwwwwwwwwppP.",
            ".rwwpppwwwwwwpppP.",
            ".rwwwpppwwwppppwP.",
            "..PwwpppppppppwP..",
            "...PwwwppppppwP...",
            "....PwwwwwwwwP....",
            ".....PPPPPPPP.....",
            "......llctll......",
            ".......lctl.......",
            "........ct........",
            "........ct........",
            "........ct........",
            "........ct........",
            "........ct........",
            "........ct........",
            "........ct........",
        ]);
        // 도넛 행성 (분홍 아이싱 + 스프링클)
        Add(set, "deco_donut_planet", 1.0, Base,
        [
            "...........pppppp...........",
            ".......ppppppsppppppp.......",
            "....ppppppyppppppspppppp....",
            "...pppppppppppppppppppppp...",
            "..ppppmppppppppppppppmpppp..",
            ".ppppppppppppppppppppppppsp.",
            ".ppypppppppbbbbbbpppppppppp.",
            "pppppppppppp....pppppppwpppp",
            ".pptpyppttpttttttppmptppppt.",
            ".ptttppttttpttptttppttttptt.",
            "..tttttttttttttttttttttttt..",
            "...tttttttttttttttttttttt...",
            "....bbbbbbbbbbbbbbbbbbbb....",
            ".......bbbbbbbbbbbbbb.......",
            "...........bbbbbb...........",
        ]);
        // 포장 사탕
        Add(set, "deco_wrapped_candy", 1.0, wrappedCandyMap,
        [
            ".u.......wwmmww.......u.",
            ".uuu....wwmmwwmT....uuu.",
            ".ullu..wwmmwwmmhh..ullu.",
            ".ullluuwmmwwmmwwTuulllu.",
            ".ulllllmmwwmmwwmTlllllu.",
            ".ullluumwwmmwwmmhuulllu.",
            ".ullu..wwmmwwmmhh..ullu.",
            ".uuu....mmwwmmwh....uuu.",
            ".u.......hhTThh.......u.",
        ]);
        // 스프링클 (빙글)
        Add(set, "star_sprinkle_small", 0.5, Base,
        [
            "p..",
            ".p.",
            "..p",
        ],
        [
            "..m",
            ".m.",
            "m..",
        ]);
        // 별사탕 (반짝)
        Add(set, "star_konpeito_big", 0.6, Base,
        [
            ".p.p.",
            "ppwpp",
            ".ppp.",
            "ppppp",
            ".p.p.",
        ],
        [
            "..p..",
            "pwppp",
            ".ppp.",
            "ppppp",
            "..p..",
        ]);
    }
}
