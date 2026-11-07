namespace CozyOrbit.Core.Art;

// 상점 배경 테마들. 장식은 탐사선 자리(왼쪽 35%, 세로 가운데)를 비워 두고, 먼 것일수록 Parallax 를 작게.
public static partial class SceneThemes
{
    static partial void AddThemes(List<SceneTheme> list)
    {
        // 노을 우주 — 복숭아빛 하늘, 지평선에 걸린 해, 나무와 크림 벽 연구소
        list.Add(new SceneTheme
        {
            Id = "sunset",
            SkyTop = Palette.SunsetSkyTop,
            SkyBottom = Palette.SunsetSkyBottom,
            StarDot = Palette.SunsetStar,
            Wall = Palette.SunsetWall,
            WallDark = Palette.SunsetWallDark,
            Line = Palette.Outline,
            Window = Palette.SunsetWindow,
            Lamp = Palette.Butter,
            Floor = Palette.SunsetFloor,
            FloorLight = Palette.SunsetFloorLight,
            FloorDark = Palette.SunsetFloorDark,
            Decorations =
            [
                new("deco_sunset_sun", 0.62, 0.72, 0.02),
                new("deco_sunset_planet", 0.86, 0.10, 0.04),
                new("deco_pink_cloud", 0.42, 0.06, 0.12),
                new("deco_pink_cloud", 0.02, 0.03, 0.18),
            ],
        });

        // 민트 오로라 — 남색 밤하늘에 초록 커튼, 눈 내린 얼음빛 연구소
        list.Add(new SceneTheme
        {
            Id = "aurora",
            SkyTop = Palette.AuroraSkyTop,
            SkyBottom = Palette.AuroraSkyBottom,
            StarDot = Palette.AuroraStar,
            Wall = Palette.AuroraWall,
            WallDark = Palette.AuroraWallDark,
            Line = Palette.Outline,
            Window = Palette.AuroraWindow,
            Lamp = Palette.Butter,
            Floor = Palette.AuroraFloor,
            FloorLight = Palette.AuroraFloorLight,
            FloorDark = Palette.AuroraFloorDark,
            Decorations =
            [
                new("deco_aurora", 0.38, 0.02, 0.03),
                new("deco_aurora", 0.76, 0.16, 0.05),
                new("deco_snow_moon", 0.88, 0.62, 0.02),
            ],
        });

        // 딸기 우유 — 온통 분홍, 우유빛 별, 하트와 딸기
        list.Add(new SceneTheme
        {
            Id = "strawberry_milk",
            SkyTop = Palette.MilkSkyTop,
            SkyBottom = Palette.MilkSkyBottom,
            StarDot = Palette.MilkStar,
            StarSmall = "star_milk_small",
            StarBig = "star_milk_big",
            Wall = Palette.MilkWall,
            WallDark = Palette.MilkWallDark,
            Line = Palette.Outline,
            Window = Palette.MilkWindow,
            Lamp = Palette.Butter,
            Floor = Palette.MilkFloor,
            FloorLight = Palette.MilkFloorLight,
            FloorDark = Palette.MilkFloorDark,
            Decorations =
            [
                new("deco_milk_carton", 0.84, 0.10, 0.05),
                new("deco_heart", 0.42, 0.06, 0.15),
                new("deco_strawberry", 0.58, 0.64, 0.2),
                new("deco_heart", 0.03, 0.80, 0.25),
            ],
        });

        // 우주 바다 — 별 대신 거품, 해파리와 먼 고래, 둥근 창 같은 물빛 연구소에 모래 바닥
        list.Add(new SceneTheme
        {
            Id = "deep_sea",
            SkyTop = Palette.SeaSkyTop,
            SkyBottom = Palette.SeaSkyBottom,
            StarDot = Palette.SeaStar,
            StarSmall = "star_bubble_small",
            StarBig = "star_bubble_big",
            Wall = Palette.SeaWall,
            WallDark = Palette.SeaWallDark,
            Line = Palette.Outline,
            Window = Palette.SeaWindow,
            Lamp = Palette.Butter,
            Floor = Palette.SeaFloor,
            FloorLight = Palette.SeaFloorLight,
            FloorDark = Palette.SeaFloorDark,
            Decorations =
            [
                new("deco_space_whale", 0.48, 0.06, 0.03),
                new("deco_coral", 0.70, 0.80, 0.3),
                new("deco_jellyfish", 0.90, 0.30, 0.12),
                new("deco_jellyfish", 0.03, 0.68, 0.18),
            ],
        });

        // 사탕 은하 — 보랏빛 분홍 하늘, 스프링클과 별사탕 별, 막대사탕·도넛 행성, 사탕 줄무늬 바닥
        list.Add(new SceneTheme
        {
            Id = "candy",
            SkyTop = Palette.CandySkyTop,
            SkyBottom = Palette.CandySkyBottom,
            StarDot = Palette.CandyStar,
            StarSmall = "star_sprinkle_small",
            StarBig = "star_konpeito_big",
            Wall = Palette.CandyWall,
            WallDark = Palette.CandyWallDark,
            Line = Palette.Outline,
            Window = Palette.CandyWindow,
            Lamp = Palette.Butter,
            Floor = Palette.CandyFloor,
            FloorLight = Palette.CandyFloorLight,
            FloorDark = Palette.CandyFloorDark,
            Decorations =
            [
                new("deco_lollipop", 0.50, 0.02, 0.12),
                new("deco_donut_planet", 0.78, 0.06, 0.04),
                new("deco_wrapped_candy", 0.62, 0.84, 0.25),
                new("deco_wrapped_candy", 0.04, 0.06, 0.18),
            ],
        });
    }
}
