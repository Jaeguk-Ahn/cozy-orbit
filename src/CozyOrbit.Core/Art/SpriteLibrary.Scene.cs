namespace CozyOrbit.Core.Art;

// 연구소·배경 별·반짝이
public static partial class SpriteLibrary
{
    private static void AddScene(Dictionary<string, PixelSprite> set)
    {
        // 연구소 — 소행성에 판 굴. 동그란 문, 깜빡이는 창, 안테나
        Add(set, "station", 0.9, Base,
        [
            "......................................kkk.......",
            ".....................................kwwk.......",
            ".....................................kRRk.......",
            "..........y...........................kkk.......",
            "..........k............................k........",
            "......kk..k..kk........................k........",
            "......kwhhkhhik................kkkkkkkkk........",
            ".......khhhhik...............kkmmmmmmmkik.......",
            "........kkkkkkkk............kmmmmmmmmmmmmk......",
            ".......kmmkmmmmmkkkkkkkkkkkkmmmmmmmmmmmmmmk.....",
            "......kmmkjkmmmmmmmmmmmmmpmmmmmmmmmmmmmmmmk.....",
            ".....kmmmmmmmmpmmmmmmmmmmmmmmmmmmmmmmmmmmmk.....",
            ".....kmmmTmTTTmmmmmmmmmTmTTTmmmmmmmmmmmTTTk.....",
            ".....kmTTiTiiiTTmmmmmTTiTiiiTTmmmmmmTTTiiiky....",
            "....kTTiiiiiiiiiTmTTTiiiiiiiiiTmTTTTiiiiiiik....",
            "...yhiiiiiiiiiiiiTikkkkkiiiiiiiTiiiiiiiiiiiik...",
            "..khiiiiiiiiiiiiikktttttkkiiiiiikkkkkiiiiiiiik..",
            ".khiiiiiiiiiiiiiiktkkkkktkiiiiiktttttkiiiiiiiik.",
            ".kiiiiikkkkiiiiikttkdbdkttkiiiktpwwyptkiiiiiiik.",
            ".kiiiikttttkiiiiktkbdbdbktkiiiktpyyyptkiiiiiiik.",
            ".kiiiiktggtkiiiiktkbdbdbktkiiiktpyyyptkiijjiiik.",
            ".kiiiiktggtkiiiiktkbdbdbktkiiiktpyyyptkijjjjiik.",
            ".kiiiikttttkiiiiktkbdbdbktkiiiktpyyyptkihjjhijk.",
            ".kiiiiikkkkiiiiiktkbdbdbktkiiiiktttttkiiihhijjk.",
            "..kiiiiiiiiiiiiiktkbdbdbktkiiiiikkkkkiiiiiijjk..",
            "...kiiiiiiiiiiiiktkbdbdyktkiiiiiiiiiiiiiiijjk...",
            "....kjiijjjjiiiiktkbdbdgktkiiiiiiiiiiiiijjjk....",
            ".....kkjjjjjjiiiktkbdbdbktkiiiiiiiiijjjjjkk.....",
            ".......kkjjjhiiiktkbdbdbktkiiiiiiiijjjjkk.......",
            "........hkkkjjjiktkbdbdbktkiiiijjjjhkkkh........",
            "............kkkkkkkkkkkkkkkjjjjjkkkkhhh.........",
            "..............khhhhhhhhhhhhhkkkk................",
        ],
        [
            "......................................kkk.......",
            ".....................................kpwk.......",
            ".....................................kppk.......",
            "..........y...........................kkk.......",
            "..........k............................k........",
            "......kk..k..kk........................k........",
            "......kwhhkhhik................kkkkkkkkk........",
            ".......khhhhik...............kkmmmmmmmkik.......",
            "........kkkkkkkk............kmmmmmmmmmmmmk......",
            ".......kmmkmmmmmkkkkkkkkkkkkmmmmmmmmmmmmmmk.....",
            "......kmmkjkmmmmmmmmmmmmmpmmmmmmmmmmmmmmmmk.....",
            ".....kmmmmmmmmpmmmmmmmmmmmmmmmmmmmmmmmmmmmk.....",
            ".....kmmmTmTTTmmmmmmmmmTmTTTmmmmmmmmmmmTTTk.....",
            ".....kmTTiTiiiTTmmmmmTTiTiiiTTmmmmmmTTTiiiky....",
            "....kTTiiiiiiiiiTmTTTiiiiiiiiiTmTTTTiiiiiiik....",
            "...yhiiiiiiiiiiiiTikkkkkiiiiiiiTiiiiiiiiiiiik...",
            "..khiiiiiiiiiiiiikktttttkkiiiiiikkkkkiiiiiiiik..",
            ".khiiiiiiiiiiiiiiktkkkkktkiiiiiktttttkiiiiiiiik.",
            ".kiiiiikkkkiiiiikttkdbdkttkiiiktpwygptkiiiiiiik.",
            ".kiiiikttttkiiiiktkbdbdbktkiiiktpgggptkiiiiiiik.",
            ".kiiiiktyytkiiiiktkbdbdbktkiiiktpgggptkiijjiiik.",
            ".kiiiiktyytkiiiiktkbdbdbktkiiiktpgggptkijjjjiik.",
            ".kiiiikttttkiiiiktkbdbdbktkiiiktpgggptkihjjhijk.",
            ".kiiiiikkkkiiiiiktkbdbdbktkiiiiktttttkiiihhijjk.",
            "..kiiiiiiiiiiiiiktkbdbdbktkiiiiikkkkkiiiiiijjk..",
            "...kiiiiiiiiiiiiktkbdbdyktkiiiiiiiiiiiiiiijjk...",
            "....kjiijjjjiiiiktkbdbdgktkiiiiiiiiiiiiijjjk....",
            ".....kkjjjjjjiiiktkbdbdbktkiiiiiiiiijjjjjkk.....",
            ".......kkjjjhiiiktkbdbdbktkiiiiiiiijjjjkk.......",
            "........hkkkjjjiktkbdbdbktkiiiijjjjhkkkh........",
            "............kkkkkkkkkkkkkkkjjjjjkkkkhhh.........",
            "..............khhhhhhhhhhhhhkkkk................",
        ]);

        // 작은 별 (반짝)
        Add(set, "star_small", 0.7, Base,
        [
            ".y.",
            "ywy",
            ".y.",
        ],
        [
            "...",
            ".y.",
            "...",
        ]);

        // 큰 별 (반짝)
        Add(set, "star_big", 0.6, Base,
        [
            "..y..",
            "..y..",
            "yywyy",
            "..y..",
            "..y..",
        ],
        [
            ".....",
            ".y.y.",
            "..w..",
            ".y.y.",
            ".....",
        ]);

        // 부스트 반짝이 (생겨남 → 퍼짐 → 사라짐)
        Add(set, "sparkle", 0.1, Base,
        [
            ".....",
            "..y..",
            ".ywy.",
            "..y..",
            ".....",
        ],
        [
            "..y..",
            "..w..",
            "ywwwy",
            "..w..",
            "..y..",
        ],
        [
            "y...y",
            ".....",
            "..y..",
            ".....",
            "y...y",
        ]);
    }
}
