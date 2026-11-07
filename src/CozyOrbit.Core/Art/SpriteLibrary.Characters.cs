namespace CozyOrbit.Core.Art;

// 햄찌 크루 + 선장 복장 (탐사선은 SpriteLibrary.Ships.cs)
public static partial class SpriteLibrary
{
    private static void AddCharacters(Dictionary<string, PixelSprite> set)
    {
        // 햄찌: F 털, S 털 그림자, L 배·주둥이, E 귀 안쪽·발
        // 프레임 0 대기, 1 깜빡, 2 걸음 A, 3 걸음 B. 0~12 행(머리·얼굴·귀)은 모든 프레임이 같다 (깜빡은 눈만) — 복장이 어긋나지 않게.
        // 통통 튀기·좌우 이동·뒤집기는 화면 쪽이 한다. 여기서는 발만 움직인다.
        string[] ham0 =
        [
            "................",
            "..kk..kkkk..kk..",
            ".kEEkkFFFFkkEEk.",
            ".kEFFFFFFFFFFEk.",
            "kFFFFFFFFFFFFFSk",
            "kFFFFFFFFFFFFFSk",
            "kFFwkFFFFFFwkFSk",
            "kFFkkFLLLLFkkFSk",
            "kFppFLLrrLLFppSk",
            "kFFFFLLKKLLFFFSk",
            "kFFFFFLLLLFFFFSk",
            "kSFFFLLLLLLFFSSk",
            ".kSFLLLLLLLLFSk.",
            "..kSSLLLLLLSSk..",
            "...kkkkkkkkkk...",
            "....kEk..kEk....",
        ];
        var hamBlink = Patch(ham0, (6, "kFFFFFFFFFFFFFSk"));
        // 걸음: 한쪽 발을 옆으로 살짝 차올리고, 다른 발로 선다
        var hamStepA = Patch(ham0, (14, ".kEkkkkkkkkkk..."), (15, ".........kEk...."));
        var hamStepB = Patch(ham0, (14, "...kkkkkkkkkkEk."), (15, "....kEk........."));
        (uint Fur, uint Shade, uint Belly, uint Ear)[] furs =
        [
            (Palette.Gold, Palette.Apricot, Palette.Cream, Palette.Pink),       // 0 황금
            (Palette.Cream, Palette.Tan, Palette.White, Palette.Pink),          // 1 크림
            (Palette.Gray, Palette.GrayDark, Palette.GrayLight, Palette.Pink),  // 2 회색
            (Palette.Brown, Palette.BrownDark, Palette.Tan, Palette.Rose),      // 3 초코
            (Palette.White, Palette.GrayLight, Palette.White, Palette.Pink),    // 4 흰둥이
            (Palette.Apricot, Palette.Brown, Palette.White, Palette.Pink),      // 5 살구 + 흰 배
        ];
        for (var i = 0; i < furs.Length; i++)
        {
            var (fur, shade, belly, ear) = furs[i];
            var map = With(('F', fur), ('S', shade), ('L', belly), ('E', ear));
            Add(set, $"hamster_{i}", 0.3, map, ham0, hamBlink, hamStepA, hamStepB);
        }

        AddOutfits(set, ham0);
    }

    /// <summary>
    /// 선장 복장: 햄찌 위에 겹쳐 그리는 20×22 스프라이트. 햄찌 왼쪽 위 = (2, 6) (Skins.OutfitOriginX/Y).
    /// 프레임은 1장, 또는 햄찌와 같은 번호의 4장 (대기·깜빡·걸음 A·걸음 B).
    /// 털 위에 얹는 부분은 늘 외곽선으로 감싼다 — 여섯 털색 어디에 얹어도 보이게.
    /// </summary>
    private static void AddOutfits(Dictionary<string, PixelSprite> set, string[] ham)
    {
        var blank = Enumerable.Repeat(new string('.', Game.Skins.OutfitWidth), Game.Skins.OutfitHeight).ToArray();
        string[] Rows(params (int Row, string Text)[] rows) => Patch(blank, rows);

        // 아래 격자의 '.' 아닌 칸을 위 격자에 덮어쓴다
        static string[] Stack(string[] under, string[] over)
        {
            var rows = new string[under.Length];
            for (var y = 0; y < rows.Length; y++)
            {
                var chars = under[y].ToCharArray();
                for (var x = 0; x < chars.Length; x++)
                    if (over[y][x] != '.') chars[x] = over[y][x];
                rows[y] = new string(chars);
            }
            return rows;
        }

        // 망토: 몸 바깥 양옆에만 그려 등 뒤에 있는 것처럼 보인다. C 바탕, D 그림자.
        // 걸음마다 차올린 발 쪽 자락이 들린다 (발을 가리지 않게).
        var cape = Rows(
            (15, ".k................k."),
            (16, "kC................Ck"),
            (17, "kC................Ck"),
            (18, "kDC..............CDk"),
            (19, "kDCC............CCDk"),
            (20, "kDCCC..........CCCDk"),
            (21, "kkkkkk........kkkkkk"));
        var capeA = Patch(cape, (20, "kkk............CCCDk"), (21, "..............kkkkkk"));
        var capeB = Patch(cape, (20, "kDCCC............kkk"), (21, "kkkkkk.............."));

        // 포근 목도리: R 베리, X 베리 그림자. 걸을 때 꼬리 끝이 살랑.
        // 꼬리는 몸 오른쪽 바깥으로 늘어져 차올린 발을 가리지 않는다.
        var scarf = Rows(
            (16, "..kkkkkkkkkkkkkkkk.."),
            (17, ".kRRRwRRRRRRRRwRRRk."),
            (18, ".kXXXwXXXXXXXXwXXRRk"),
            (19, "..kkkkkkkkkkkkkkkwwk"),
            (20, "................kRRk"),
            (21, "................kRRk"));
        var scarfSway = Patch(scarf, (20, "...............kRRk."), (21, "...............kRRk."));
        Add(set, "outfit_scarf", 0.3, With(('X', Palette.BerryDeep)), scarf, scarf, scarfSway, scarf);

        // 연구 가운 + 이마 위 고글
        Add(set, "outfit_labcoat", 0.3, Base, Rows(
            (7, ".....kkk....kkk....."),
            (8, "....kwsskjjkwssk...."),
            (9, "...jksssk..kssskj..."),
            (10, ".....kkk....kkk....."),
            (16, "..kwwwwk....kwwwwk.."),
            (17, "..khwwwk....kwewhk.."),
            (18, "...khwwk....khhhk..."),
            (19, "....khwk....kwhk....")));

        // 꿀잠 잠옷: 방울 달린 수면 모자 + 줄무늬 잠옷
        Add(set, "outfit_pajama", 0.3, Base, Rows(
            (3, "........kkkkkk......"),
            (4, "......kkssssssk....."),
            (5, ".....kssswssssssk..."),
            (6, "....ksssssssssskssk."),
            (7, "...ksssssssswssskwwk"),
            (8, "...kssswsssssssskwck"),
            (9, "..kwwwwwwwwwwwwwwkk."),
            (10, "..kkkkkkkkkkkkkkkk.."),
            (16, "..kwwwwwwkkwwwwwwk.."),
            (17, "..kesesesesesesesk.."),
            (18, "...ksesesesesesek..."),
            (19, "....kesesesesesk....")));

        // 우주 헬멧: 하늘색 테두리 + 반짝 하이라이트만, 얼굴 위는 비운다. 머리 둘레의 빈 곳만 아주 옅은 유리색(q).
        var helmet = Rows(
            (0, ".............kRk...."),
            (1, "..............j....."),
            (2, ".......ssssss.j....."),
            (3, ".....ss......ss....."),
            (4, "...ssww........ss..."),
            (5, "..sw.............s.."),
            (6, ".sw...............s."),
            (7, ".s................s."),
            (8, "s..................s"),
            (9, "s..................s"),
            (10, "s..................s"),
            (11, "s..................s"),
            (12, "s.................ws"),
            (13, "s.................ws"),
            (14, "s..................s"),
            (15, "s..................s"),
            (16, ".kkkkkkkkkkkkkkkkkk."),
            (17, "khhwhhhhhhhhhhhhhhhk"),
            (18, ".kiiiimiiiiiiRiiiik."),
            (19, "..kkkkkkkkkkkkkkkk.."));
        for (var y = 3; y <= 15; y++)
        {
            var row = helmet[y].ToCharArray();
            int left = helmet[y].IndexOf('s'), right = helmet[y].LastIndexOf('s');
            for (var x = left + 1; x < right; x++)
            {
                int hy = y - Game.Skins.OutfitOriginY, hx = x - Game.Skins.OutfitOriginX;
                var furHere = hy >= 0 && hx >= 0 && hx < ham[0].Length && ham[hy][hx] != '.';
                if (row[x] == '.' && !furHere) row[x] = 'q';
            }
            helmet[y] = new string(row);
        }
        Add(set, "outfit_helmet", 0.3, With(('q', Palette.GlassTint)), helmet);

        // 우주 해적 선장: 삼각 모자 + 귀여운 해골 + 안대
        Add(set, "outfit_pirate", 0.3, Base, Rows(
            (3, ".......kkkkkk......."),
            (4, "..kk..kddddddk..kk.."),
            (5, ".kgdkkddwwwwddkkdgk."),
            (6, ".kgddddwkwwkwddddgk."),
            (7, ".kgdddddwwwwdddddgk."),
            (8, ".kggdddddwwdddddggk."),
            (9, "..kkggggggggggggkk.."),
            (10, "....kkkkkkkkkkkk...."),
            (11, "............k...k..."),
            (12, "............kkkk...."),
            (13, ".............kk.....")));

        // 별 마법사: 끝이 휜 뾰족 모자 + 작은 보라 망토
        var wizardHat = Rows(
            (0, "..............k....."),
            (1, ".............kuk...."),
            (2, "............kuuk...."),
            (3, "...........kuyuk...."),
            (4, "..........kuuuuk...."),
            (5, "........kuuuuuuk...."),
            (6, "......kuuuuyuuuk...."),
            (7, "....kuuyuuuuuuuk...."),
            (8, "..kkkyyyyyyyyyykkk.."),
            (9, ".kuuuuuuuuuuuuuuuuk."),
            (10, "..kkkkkkkkkkkkkkkk.."));
        var wizardMap = With(('C', Palette.Purple), ('D', Palette.PurpleDeep));
        Add(set, "outfit_wizard", 0.3, wizardMap,
            Stack(cape, wizardHat), Stack(cape, wizardHat), Stack(capeA, wizardHat), Stack(capeB, wizardHat));

        // 꼬마 왕: 금관 + 빨간 망토
        var crown = Rows(
            (3, ".....k...kk...k....."),
            (4, ".....kk.kyyk.kk....."),
            (5, ".....kykkggkkyk....."),
            (6, ".....kggggggggk....."),
            (7, ".....kgRgssgRgk....."),
            (8, ".....kyyyyyyyyk....."),
            (9, ".....kkkkkkkkkk....."));
        var kingMap = With(('C', Palette.Berry), ('D', Palette.BerryDeep));
        Add(set, "outfit_king", 0.3, kingMap,
            Stack(cape, crown), Stack(cape, crown), Stack(capeA, crown), Stack(capeB, crown));
    }
}
