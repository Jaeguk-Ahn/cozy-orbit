namespace CozyOrbit.Core.Art;

// 탐사선 스킨. id 규칙: 기본 도토리호는 probe / probe_boost, 나머지는 ship_<스킨> / ship_<스킨>_boost (Game/Skins.cs)
public static partial class SpriteLibrary
{
    private static void AddShips(Dictionary<string, PixelSprite> set)
    {
        // 도토리호 몸통 (x6~23). 오른쪽으로 날아간다 — 동그란 창 안에 햄찌가 타고 있다
        string[] probeBody =
        [
            "..................",
            "........kk........",
            ".......kdk........",
            "....kkkkdkkk......",
            "..kkttbbdbbbkk....",
            ".kttbbbdbbbdbbk...",
            "ktbbdbbbdbbbdbbk..",
            "tbbbdbbbdbbbdbbbk.",
            "bbdbbbdbbbdbbbddk.",
            "kkkkkkkkkkkkkkkkk.",
            "ktccttttttttttbk..",
            "ktccttttkkkkktbk..",
            "ktcttttkwgsgskbk..",
            "kttttttksgggskbk..",
            "kttttttkgkgkgkbk..",
            "kttttttkgprpgkbk..",
            "kttttttkgggggkbk..",
            "ktttttttkkkkkbbk..",
            ".ktttttttttbbbk...",
            "..kttttttbbbbk....",
            "...kkbbbbbbkk.....",
            ".....kkbbkk.......",
            ".......kk.........",
            "..................",
        ];
        // 꼬리 쪽 (x0~5): 모자 챙 끝·분사구·불꽃
        string[] Tail(params (int Row, string Text)[] flame)
        {
            string[] tail =
            [
                "......", "......", "......", "......", "......", "......", "......",
                ".....k", ".....k", ".....k", "......", ".....k",
                ".....h", ".....i", ".....j",
                ".....k", "......", "......", "......", "......", "......", "......", "......", "......",
            ];
            return Patch(tail, flame);
        }
        Add(set, "probe", 0.12, Base,
            Beside(Tail((12, "...ayh"), (13, ".ayywi"), (14, "...ayj")), probeBody),
            Beside(Tail((12, "..aayh"), (13, "aayywi"), (14, "..aayj")), probeBody));
        Add(set, "probe_boost", 0.08, Base,
            Beside(Tail((6, "..y..."), (11, ".aayyk"), (12, "ayywwh"), (13, "aywwwi"), (14, "ayywwj"), (15, ".aayyk"), (19, "...w..")), probeBody),
            Beside(Tail((5, ".ywy.."), (6, "..y..."), (11, "..aayk"), (12, "aayywh"), (13, "ayywwi"), (14, "aayywj"), (15, "..aayk"), (18, "...y.."), (19, "..ywy."), (20, "...y..")), probeBody),
            Beside(Tail((5, ".y...."), (11, ".aayyk"), (12, "ayywwh"), (13, "aywwwi"), (14, "ayywwj"), (15, ".aayyk"), (19, ".ywy.."), (20, "..y...")), probeBody),
            Beside(Tail((11, "..aayk"), (12, "aayywh"), (13, "ayywwi"), (14, "aayywj"), (15, "..aayk"), (20, ".y....")), probeBody));

        AddSkinShips(set, probeBody, Tail);
    }

    // 꼬리 불꽃 무늬 (x0~5, 오른쪽 끝 열은 몸통 쪽이라 비움). 분사구 중심은 도토리호처럼 13행
    private static readonly (int Row, string Text)[][] ShipFlame =
    [
        [(12, "...ay."), (13, ".ayyw."), (14, "...ay.")],
        [(12, "..aay."), (13, "aayyw."), (14, "..aay.")],
    ];
    private static readonly (int Row, string Text)[][] ShipBoostFlame =
    [
        [(6, "..y..."), (11, "..aay."), (12, ".ayyw."), (13, "aywww."), (14, ".ayyw."), (15, "..aay."), (19, "...w..")],
        [(5, ".ywy.."), (6, "..y..."), (11, ".aayy."), (12, "aayyw."), (13, "ayyww."), (14, "aayyw."), (15, ".aayy."), (18, "...y.."), (19, "..ywy."), (20, "...y..")],
        [(5, ".y...."), (11, "..aay."), (12, ".ayyw."), (13, "aywww."), (14, ".ayyw."), (15, "..aay."), (19, ".ywy.."), (20, "..y...")],
        [(11, ".aayy."), (12, "aayyw."), (13, "ayyww."), (14, "aayyw."), (15, ".aayy."), (20, ".y....")],
    ];

    // 창 안의 햄찌 얼굴 (7x7) — 도토리호 창과 같은 얼굴
    private static readonly string[] ShipWindow =
    [
        ".kkkkk.",
        "kwgsgsk",
        "ksgggsk",
        "kgkgkgk",
        "kgprpgk",
        "kgggggk",
        ".kkkkk.",
    ];

    /// <summary>격자 위에 다른 격자를 (x, y) 에 겹친다. 덮는 쪽의 '.' 은 투명</summary>
    private static string[] Over(string[] frame, int x, int y, string[] stamp)
    {
        var copy = (string[])frame.Clone();
        for (var r = 0; r < stamp.Length; r++)
        {
            var row = copy[y + r].ToCharArray();
            for (var c = 0; c < stamp[r].Length; c++)
                if (stamp[r][c] != '.') row[x + c] = stamp[r][c];
            copy[y + r] = new string(row);
        }
        return copy;
    }

    /// <summary>꼬리(x0~5) 불꽃 + 몸통(x6~23) 으로 평소/부스트 스프라이트를 만든다. 몸통 픽셀은 모든 프레임이 같다</summary>
    private static void AddFlameShip(Dictionary<string, PixelSprite> set, string id, IReadOnlyDictionary<char, uint> map, string[] body)
    {
        // 분사구(x5, 11~15행)는 모든 프레임에 같고, 불꽃은 그 왼쪽에만 그린다
        var nozzle = Patch(Enumerable.Repeat("......", 24).ToArray(), (11, ".....k"), (12, ".....h"), (13, ".....i"), (14, ".....j"), (15, ".....k"));
        string[] Tail((int Row, string Text)[] flame) => Over(nozzle, 0, 0, Patch(Enumerable.Repeat("......", 24).ToArray(), flame));
        Add(set, id, 0.12, map, ShipFlame.Select(f => Beside(Tail(f), body)).ToArray());
        Add(set, id + "_boost", 0.08, map, ShipBoostFlame.Select(f => Beside(Tail(f), body)).ToArray());
    }

    private static void AddSkinShips(Dictionary<string, PixelSprite> set, string[] probeBody, Func<(int Row, string Text)[], string[]> probeTail)
    {
        // 황금 도토리호: 도토리호 격자를 금빛으로 바꿔 칠하고 반짝이를 얹는다
        var goldMap = With(('t', Palette.Butter), ('b', Palette.Gold), ('d', Palette.ShipGoldDeep), ('c', Palette.White));
        var goldBody = Over(probeBody, 0, 0,
        [
            "..................",
            "..w...............",
            ".www........w.....",
            "..w.............w.",
            "...............www",
            "................w.",
            "..w...............",
            "..................",
        ]);
        Add(set, "ship_golden_acorn", 0.12, goldMap,
            Beside(probeTail([(12, "...ayh"), (13, ".ayywi"), (14, "...ayj")]), goldBody),
            Beside(probeTail([(12, "..aayh"), (13, "aayywi"), (14, "..aayj")]), goldBody));
        Add(set, "ship_golden_acorn_boost", 0.08, goldMap,
            Beside(probeTail([(6, "..y..."), (11, ".aayyk"), (12, "ayywwh"), (13, "aywwwi"), (14, "ayywwj"), (15, ".aayyk"), (19, "...w..")]), goldBody),
            Beside(probeTail([(5, ".ywy.."), (6, "..y..."), (11, "..aayk"), (12, "aayywh"), (13, "ayywwi"), (14, "aayywj"), (15, "..aayk"), (18, "...y.."), (19, "..ywy."), (20, "...y..")]), goldBody),
            Beside(probeTail([(5, ".y...."), (11, ".aayyk"), (12, "ayywwh"), (13, "aywwwi"), (14, "ayywwj"), (15, ".aayyk"), (19, ".ywy.."), (20, "..y...")]), goldBody),
            Beside(probeTail([(11, "..aayk"), (12, "aayywh"), (13, "ayywwi"), (14, "aayywj"), (15, "..aayk"), (20, ".y....")]), goldBody));

        // 딸기호: 초록 잎 모자, 노란 씨앗 점, 가운데 동그란 창
        string[] strawberry =
        [
            "..................",
            "..................",
            "........kk........",
            ".......kTk........",
            "...kk..kTk..kk....",
            "..kmmkkkTkkkmmk...",
            "...kmmmmTmmmmk....",
            "..kkkmmTTTmmkkk...",
            ".kprrkkmmmkkrrrk..",
            "kprrrryrrrrryrrrk.",
            "kprrrrrrrrrrrrrrrk",
            "krryrrrrrrrrryrrrk",
            "krrrrrrrrrrrrrrrrk",
            "krrrrrrrrrrrrrryrk",
            "kRrrrrrrrrrrrrrrk.",
            "kRryrrrrrrrrrrrrk.",
            ".kRrrrrrrrrrryrk..",
            ".kRRrrrrrrrrrrk...",
            "..kRRryrrrrrrk....",
            "...kRRRrrryRk.....",
            "....kkRRRRkk......",
            "......kkkk........",
            "..................",
            "..................",
        ];
        AddFlameShip(set, "ship_strawberry", Base, Over(strawberry, 5, 10, ShipWindow));

        // 레트로 로켓: 옛날 그림책 로켓을 눕힌 모양. 빨간 코끝과 지느러미, 가운데 둥근 창
        string[] retro =
        [
            "..................",
            "..................",
            "..................",
            ".kk...............",
            ".kRk..............",
            ".kRrk.............",
            "..kRrk............",
            "...kRrkkkkkk......",
            "...kkhwwwwwwkkk...",
            "..khhwwwwwwwwRkk..",
            "..khwwwwwwwwwRrrk.",
            ".kjhwwwwwwwwwRrrrk",
            "kijhwwwwwwwwwRrrrk",
            "kijhwwwwwwwwwRrrwk",
            "kijhwwwwwwwwwRrrrk",
            ".kjhwwwwwwwwwRrrrk",
            "..khwwwwwwwwwRrrk.",
            "..khhwwwwwwwwRkk..",
            "...kkhwwwwwwkkk...",
            "...kRrkkkkkk......",
            "..kRrk............",
            ".kRrk.............",
            ".kRk..............",
            ".kk...............",
        ];
        AddFlameShip(set, "ship_retro", Base, Over(retro, 5, 10, ShipWindow));

        // 고래호: 오른쪽을 보는 우주 고래. 옆구리에 창, 머리 위로 별을 뿜는다
        string[] whale =
        [
            "..................",
            "...........y......",
            "..........ywy.....",
            "...........y......",
            "..........s.s.....",
            "........kkkkkk....",
            "......kkeeeeeekk..",
            ".....keeeeeeeeeek.",
            "kk..keeeeeeeeeeeek",
            "kek.keeeeeeeeeeeek",
            "keekeeeeeeeeeeeeek",
            ".keeeeeeeeeeewkeek",
            "..keeeeeeeeeeekkek",
            "..keeeeeeeeeeeeeek",
            ".keeeeeeeeeeeepeek",
            "keekeeeeccccccccck",
            "kek.keeccccccccck.",
            "kk...keccccccck...",
            "......kkkkkkkk....",
            "..................",
            "..................",
            "..................",
            "..................",
            "..................",
        ];
        AddFlameShip(set, "ship_whale", Base, Over(whale, 5, 8, ShipWindow));

        // 찻주전자호: 주둥이는 오른쪽, 뚜껑 꼭지는 위, 손잡이 쪽(왼쪽)으로 불꽃이 나온다
        string[] teapot =
        [
            "..................",
            "..................",
            "..................",
            "........kk........",
            ".......kppk.......",
            "........kk........",
            "......kkkkkk......",
            ".....kpppppppk....",
            "....kkkkkkkkkkk...",
            "kkkkcccccccccckk.k",
            "k..kccccccccccckwk",
            "k.kcccccccccccckck",
            "k.kccccccccccccckk",
            "kkkccccccccccccck.",
            "..kcccccccccccck..",
            "..kpcpcpcpcpcpck..",
            "...kcccccccccck...",
            "....kkcccccckk....",
            "......kkkkkk......",
            "..................",
            "..................",
            "..................",
            "..................",
            "..................",
        ];
        AddFlameShip(set, "ship_teapot", Base, Over(teapot, 6, 9, ShipWindow));

        // 햄찌 UFO: 유리 돔 안에서 햄찌가 손을 흔든다. 뒤 불꽃 대신 아래로 부드러운 빛
        string[] ufo =
        [
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            ".........kkkkkkk........",
            ".......kksssssswkk......",
            "......ksssgsgssgswk.....",
            ".....kssssgggsgssssk....",
            ".....ksssgkgkggssssk....",
            ".....ksssgprpggssssk....",
            ".....kssgggggggssssk....",
            "...kkkkkkkkkkkkkkkkkk...",
            "..kkhhhhhhhhhhhhhhhhkk..",
            ".khhhwhhhwhhhwhhhwhhhhk.",
            "kiiiyiiiiyiiiiyiiiiyiiik",
            ".kjjjjjjjjjjjjjjjjjjjjk.",
            "..kkkkjjjjjjjjjjjjkkkk..",
            "......kkkkkkkkkkkk......",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
        ];
        Add(set, "ship_ufo", 0.12, Base,
            Over(ufo, 0, 19, ["........mmmmmmmm........", ".........mmmmmm.........", "..........mmmm.........."]),
            Over(ufo, 0, 19, [".......mmmmmmmmmm.......", "........mmmmmmmm........", ".........mmmmmm.........", "..........mmmm.........."]));
        string[][] ufoBoost =
        [
            [".y....mmmmmmmmmmmm......", "yvy....mmmwwwwmmm.......", ".y......mmmwwmmm........", ".........mmmmmm......y..", "..........mmmm.......v.."],
            [".....mmmmmmmmmmmmmm..y..", "..v...mmmwwwwwwmmm..ywy.", ".vwv...mmmmwwmmmm....y..", "..v.....mmmmmmmm........", ".........mmmmmm........."],
            ["......mmmmmmmmmmmm......", ".......mmmwwwwmmm.......", "...y....mmmwwmmm........", "..ywy....mmmmmm.....v...", "...y......mmmm.....vwv.."],
            [".....mmmmmmmmmmmmmm.....", "......mmmwwwwwwmmm...y..", ".......mmmmwwmmmm...ywy.", "v.......mmmmmmmm.....y..", ".........mmmmmm........."],
        ];
        Add(set, "ship_ufo_boost", 0.08, Base, ufoBoost.Select(g => Over(ufo, 0, 19, g)).ToArray());
    }
}
