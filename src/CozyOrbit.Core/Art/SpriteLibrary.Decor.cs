namespace CozyOrbit.Core.Art;

// 연구소 소품·기념품 (decor_*)
// 벽 소품은 16x12 안, 바닥 소품은 18x18 안이고 맨 아래 행이 바닥에 닿는다.
public static partial class SpriteLibrary
{
    private static void AddDecor(Dictionary<string, PixelSprite> set)
    {
        // ── 바닥: 살 수 있는 소품 ──
        // 해바라기 화분 — 웃는 해바라기, 테라코타 화분
        Add(set, "decor_plant", 1.0, Base,
        [
            ".....kkkk.....",
            "...kkyyyykk...",
            "..kyyyyyyyyk..",
            ".kyykkkkkkyyk.",
            "kyykbbbbbbkyyk",
            "kyykbkbbkbkyyk",
            "kgykpbbbbpkygk",
            ".kgykbbbbkygk.",
            ".kgggkkkkgggk.",
            "..kggggggggk..",
            "...kkggggkk...",
            ".kmmkkmTkkmTk.",
            "..kk.kmTk.kk..",
            ".kkkkkkkkkkkk.",
            ".kooaaaaaaabk.",
            "..kbbbbbbbbk..",
            "..kaaaaaaabk..",
            "...kkkkkkkk...",
        ]);

        // 작은 책장 — 알록달록 책 두 칸, 위에 작은 화분과 별 장식
        Add(set, "decor_bookshelf", 1.0, Base,
        [
            "..kk.kk.........",
            ".kmmkmTk........",
            "..kkTkk......k..",
            "..kaaak.....kyk.",
            "kkkkkkkkkkkkkkkk",
            "kttttttttttttttk",
            "kbkkkkkkddddddbk",
            "kbRRkeekkkkdddbk",
            "kbRRkeekyykdddbk",
            "kbRRkeekyykdddbk",
            "kttttttttttttttk",
            "kbkkkkkkddddddbk",
            "kbllkppkdkkkkkbk",
            "kbllkppkdkmmmkbk",
            "kbllkppkdkeeekbk",
            "kttttttttttttttk",
            "kkkkkkkkkkkkkkkk",
            ".kbk........kbk.",
        ]);

        // 망원경 — 하늘색 경통, 나무 삼각대
        Add(set, "decor_telescope", 1.0, Base,
        [
            "............kk..",
            "..........kkwsk.",
            "........kkeeesk.",
            "......kkeegeekk.",
            "....kkueeegekk..",
            "...kuueeeekk....",
            "...kuuekkk......",
            "....kkk.kgk.....",
            ".......kbk......",
            "......kbbbk.....",
            ".....kbkbkbk....",
            ".....kbkbkbk....",
            "....kbkkbkkbk...",
            "...kbk.kbk.kbk..",
            "...kbk.kbk.kbk..",
            "..kbk..kbk..kbk.",
            "..kbk..kbk..kbk.",
            "..kkk..kkk..kkk.",
        ]);

        // 빈백 쿠션 — 폭신하게 꺼진 분홍 빈백
        Add(set, "decor_beanbag", 1.0, Base,
        [
            ".....kkkkkkk......",
            "...kkpwwppppkk....",
            "..kpwwpppppppk....",
            ".kppwppppppppkk...",
            ".kppppppppppppkk..",
            "kpppprpppppprpppk.",
            "kppppprrrrrppppppk",
            "kpppppppppppppprk.",
            "kppppppppppppppprk",
            "krppppppppppppprrk",
            ".krrppppppppprrrk.",
            "..kkkkkkkkkkkkkk..",
        ]);

        // 어항 — 주황 물고기 한 마리, 보글보글 거품 (2프레임)
        string[] aquarium =
        [
            ".kkkkkkkkkkkkkk.",
            "kwsssssssssssssk",
            "kwsssssssssssssk",
            "kssssssssssssssk",
            "ksssskkkskkssssk",
            "kssskkaakakssssk",
            "kssskaaaaakssssk",
            "ksTsskkkskkssssk",
            "keTTeeeeeeeeeTek",
            "kTTeeeeeeeeeTTek",
            "kcctccccctcccctk",
            "kkkkkkkkkkkkkkkk",
            "kttttttttttttttk",
            "kbbbbbbbbbbbbbbk",
            ".kbk........kbk.",
            ".kkk........kkk.",
        ];
        Add(set, "decor_aquarium", 0.6, Base,
            Patch(aquarium, (2, "kwsssssssssswssk"), (5, "kssskkaakaksswsk")),
            Patch(aquarium, (1, "kwssssssssssswsk"), (3, "ksssssssssswsssk")));

        // 쳇바퀴 — 민트 바퀴, 나무 받침
        string[] wheelTop =
        [
            "......kkkkkk......",
            "....kkmmmmmmkk....",
            "...kmmkkkkkkmmk...",
            "..kmmk......kmmk..",
            ".kmk.K......K.kmk.",
            ".kmk..K....K..kmk.",
            "kmk....K..K....kmk",
            "kmk.....kk.....kmk",
        ];
        Add(set, "decor_wheel", 1.0, Base,
            [.. wheelTop, .. wheelTop.Reverse(), "....kbk....kbk....", "..kkkkkkkkkkkkkk.."]);

        // 티 테이블 — 분홍 찻주전자와 찻잔
        Add(set, "decor_tea_table", 1.0, Base,
        [
            "..............h...",
            ".....kk........h..",
            "....kppk......h...",
            "...kkkkkk.........",
            "k.kwpppppkkk.kkkk.",
            "kpppprpppk.k.kwskk",
            ".kkppppppkkk.kssk.",
            ".kkkkkkkkkkkkkkkk.",
            "kttttttttttttttttk",
            "kbbbbbbbbbbbbbbbbk",
            ".kkkkkkkkkkkkkkkk.",
            "..kbk........kbk..",
            "..kbk........kbk..",
            "..kkk........kkk..",
        ]);

        // ── 벽: 살 수 있는 소품 ──
        // 별자리 포스터 — 북두칠성
        Add(set, "decor_star_poster", 1.0, Base,
        [
            ".....kRk....",
            "kkkkkkkkkkkk",
            "kNNNNNNNNNNk",
            "kNwuNNNNNNNk",
            "kNNNyuNNNNNk",
            "kNNNNNyNNNNk",
            "kNNNNNNuNNNk",
            "kNNNNNNyuuyk",
            "kNNwNNNuNNuk",
            "kNNNNNNyuuyk",
            "kNNNNNNNNNNk",
            "kkkkkkkkkkkk",
        ]);

        // 도토리 시계 — 째깍째깍 분침 (2프레임)
        string[] clock =
        [
            "......k.....",
            ".....kd.....",
            "..kkkkkkkk..",
            ".kbbdbbdbbk.",
            "kbdbbdbbdbbk",
            "kkkkkkkkkkkk",
            ".kttttttttk.",
            ".ktwwwwwwtk.",
            ".ktwwkwwwtk.",
            ".ktwwkkkwtk.",
            "..ktwwwwtk..",
            "...kkkkkk...",
        ];
        Add(set, "decor_clock", 1.0, Base,
            clock,
            Patch(clock, (9, ".ktwwkwwwtk."), (10, "..ktwkwwtk..")));

        // 꼬마 전구 — 늘어진 전선에 통통한 전구 셋, 번갈아 반짝 (2프레임)
        // A~C 는 전구 자리: 프레임마다 밝은 색·차분한 색을 바꿔 끼운다
        string[] lights =
        [
            "K..............K",
            ".KKK........KKK.",
            "..kkKK....KKkk..",
            ".kAAk.KKKK.kCCk.",
            ".kAAk..kk..kCCk.",
            "..kk..kBBk..kk..",
            "......kBBk......",
            ".......kk.......",
        ];
        Add(set, "decor_string_lights", 0.5, Base,
            DecorSwap(lights, ('A', 'y'), ('B', 'r'), ('C', 's')),
            DecorSwap(lights, ('A', 'g'), ('B', 'p'), ('C', 'e')));

        // 벽 선반 화분 — 분홍 화분에서 양쪽으로 늘어진 덩굴
        Add(set, "decor_shelf_plant", 1.0, Base,
        [
            ".....kkkk.....",
            "...kkmmwmkk...",
            "..kmmTmmTmmk..",
            ".kmTmmmmmmTmk.",
            ".kTkkkkkkkkTk.",
            "..T.kwpppk.T..",
            ".m..kppprk..m.",
            "kTkkkkkkkkkkTk",
            "kmttttttttttmk",
            "kTkkkkkkkkkkTk",
            ".m.kbk..kbk.m.",
            ".T..k....k..T.",
        ]);

        // ── 바닥: 기념품 ──
        // 치즈 쿠션 — 한 입 베어 문 치즈 달 쿠션
        Add(set, "decor_cheese_cushion", 1.0, Base,
        [
            "....kkkkkk......",
            "..kkwyyyyyk..k..",
            ".kwyyyyyyyykkgk.",
            "kwyyygyyyyyyyygk",
            "kyyyyyyyyyyyggyk",
            "kyyyykyyyykyyyyk",
            "kgyypyyKKyypyygk",
            "kgyyyyyyyyyyyggk",
            ".kggyyyyyyyyggk.",
            "..kkkkkkkkkkkk..",
        ]);

        // 위성 모형 — 반창고 붙인 낡은 인공위성, 나무 받침
        Add(set, "decor_satellite_model", 1.0, Base,
        [
            ".........Rw.......",
            ".........k........",
            "........k.........",
            "......kkkkkk......",
            "kkkk.kwhhhhhk.kkkk",
            "kesk.khkhhkhk.kesk",
            "keskjkhkhhkhkjkesk",
            "kesk.kphhhhpk.kesk",
            "kkkk.khhkkhhk.kkkk",
            ".....khhhhttk.....",
            "......kkkkkk......",
            ".......kjk........",
            ".......kjk........",
            ".......kjk........",
            "...kkkkkkkkkkkk...",
            "..kttttgggtttbbk..",
            "..kbbbbbbbbbbbbk..",
            "...kkkkkkkkkkkk...",
        ]);

        // 오르골 — 거울 달린 뚜껑을 열면 음표가 퐁
        Add(set, "decor_music_box", 1.0, Base,
        [
            "...kkkkkkkkkk.vv.",
            "...kbsswsssbk.v..",
            "...kbswssssbkvv..",
            "...kbssssssbkvv..",
            "..kkkkkkkkkkkk...",
            "..krrrrrrrrrrk...",
            ".kkkkkkkkkkkkkk..",
            ".kttttttttttttk..",
            ".ktttttyytttttkk.",
            ".kttttyyyyttttkjk",
            ".ktttttyytttttkk.",
            ".kttttttttttttk..",
            ".kbbbbbbbbbbbbk..",
            ".kkkkkkkkkkkkkk..",
        ]);

        // 황금 도토리 트로피 — 반짝이는 황금 도토리, 나무 받침
        Add(set, "decor_golden_acorn", 1.0, Base,
        [
            "......k....w..",
            ".....kd...wyw.",
            "...kkkkkkkk.w.",
            "..kggaggaggk..",
            ".kaggaggaggak.",
            ".kkkkkkkkkkkk.",
            "..kwyyyyyyyk..",
            "..kywyyyyyyk..",
            "..kyyyyyyygk..",
            "...kyyyyygk...",
            "....kyyygk....",
            ".....kkkk.....",
            ".....kggk.....",
            ".....kggk.....",
            "..kkkkkkkkkk..",
            "..kbbbbbbbbk..",
            "..kbbbggbbbk..",
            "..kkkkkkkkkk..",
        ]);

        // 토성 램프 — 고리 두른 토성이 은은하게 빛나는 스탠드
        Add(set, "decor_saturn_lamp", 1.0, Base,
        [
            "......kkkk......",
            "....kkwyyykk....",
            "...kwyyyyyygkkk.",
            "...kyyyyyyygkwwk",
            "...kggggggwwwttk",
            ".kkkyywwwwtttkk.",
            "kwwwwwttttygk...",
            "ktttttyyyyygk...",
            ".kkkkkyyygkk....",
            "......kkkk......",
            "......kjjk......",
            "......kjjk......",
            "......kjjk......",
            "......kjjk......",
            ".....kkkkkk.....",
            "...kkllllllkk...",
            "...kuuuuuuuuk...",
            "....kkkkkkkk....",
        ]);

        // 달 지구본 — 졸린 달이 황동 자오선 고리 안에
        Add(set, "decor_moon_globe", 1.0, Base,
        [
            "....kkkk..g...",
            "..kkhhhhkk.g..",
            ".kwhhhhiihk.g.",
            "khhiihhhhhikg.",
            "khiihhhhhhikg.",
            "khhKKhhKKhikg.",
            "khphhhhhhpikg.",
            ".khhhhhhiik.g.",
            "..kkiiiikk.g..",
            "....kkkk..g...",
            "....kggk.g....",
            "....kggkg.....",
            "....kggkg.....",
            ".kkkkkkkkkk...",
            ".kttttttbbk...",
            ".kbbbbbbbbk...",
            "..kkkkkkkk....",
        ]);

        // 게 인형 — 게 성운처럼 주황 몸에 하늘색 얼룩
        Add(set, "decor_crab_plush", 1.0, Base,
        [
            ".k.k........k.k.",
            "kakak......kakak",
            "kaaak......kaaak",
            ".kak.kkkkkk.kak.",
            "..kkasaaaasakk..",
            ".kaawkaaaawkaak.",
            ".kaakkaaaakkaak.",
            ".kapaaaKKaaapak.",
            ".kaaaaaaaaaaaRk.",
            "..kRaaaaaaaaRk..",
            "..kkkkkkkkkkkk..",
            ".kak........kak.",
        ]);

        // ── 벽: 기념품 ──
        // 별 등불 — 별을 품은 종이등, 불빛이 일렁인다 (2프레임)
        string[] lantern =
        [
            ".....K.....",
            "....kkk....",
            "..kkoookk..",
            ".koaoooaok.",
            "koaoowooaok",
            "koaoyyyoaok",
            "koayywyyaok",
            "koaoyyyoaok",
            "koaoyoyoaok",
            ".koaoooaok.",
            "..kkkkkkk..",
            ".....R.....",
        ];
        Add(set, "decor_star_lantern", 0.7, Base,
            lantern,
            DecorSwap(lantern, ('o', 'a'), ('a', 'R')));

        // 고향 편지 액자 — 하트 봉인 편지
        Add(set, "decor_home_letter", 1.0, Base,
        [
            "kkkkkkkkkkkkkkkk",
            "kttttttttttttttk",
            "ktkkkkkkkkkkkktk",
            "ktkccccccccccktk",
            "ktkKccccccccKktk",
            "ktkwKccccccKwktk",
            "ktkwwKoRRRKwwktk",
            "ktkwwwKRRKwwwktk",
            "ktkwwwwwwwwwwktk",
            "ktkkkkkkkkkkkktk",
            "kttttttttttttttk",
            "kkkkkkkkkkkkkkkk",
        ]);

        // 골든 레코드 — 보이저의 황금 음반
        Add(set, "decor_golden_record", 1.0, Base,
        [
            "kkkkkkkkkkkk",
            "kNNNggggNNNk",
            "kNggaaaaggNk",
            "kNgwggggagNk",
            "kgagyyyygagk",
            "kgagykkygagk",
            "kgagykkygagk",
            "kgagyyyygagk",
            "kNgaggggagNk",
            "kNggaaaaggNk",
            "kNNNggggNNNk",
            "kkkkkkkkkkkk",
        ]);

        // 일곱 별 가랜드 — 플레이아데스 일곱 자매
        Add(set, "decor_pleiades_garland", 1.0, Base,
        [
            "K.............K",
            ".KKK.......KKK.",
            ".s.KKKKKKKKK.s.",
            "swsK.s.K.s.Ksws",
            ".s.KswsKswsK.s.",
            "...s.s.K.s.s...",
            "..sws..s..sws..",
            "...s..sws..s...",
            ".......s.......",
        ]);

        // 밀코메다 포스터 — 꼭 껴안은 두 나선 은하
        Add(set, "decor_milkomeda_poster", 1.0, Base,
        [
            "kkkkkkkkkkkkkk",
            "kNNNNyNNNNNNNk",
            "kNNNNNNNsssNNk",
            "kNNNNNNNNNNsNk",
            "kNNpppNsNwwNsk",
            "kNpNNNNsNwwNsk",
            "kpNyyNpNsNNNNk",
            "kpNyyNpNNsssNk",
            "kNNNNpNNNNNyNk",
            "kNpppNNNNNNNNk",
            "kNNNNNNNNNNNNk",
            "kkkkkkkkkkkkkk",
        ]);

        // 블랙홀 사진 — M87* 첫 사진을 붙인 즉석 사진
        Add(set, "decor_black_hole_photo", 1.0, Base,
        [
            "kkkkkkkkkkkk",
            "kwwwwwwwwwwk",
            "kwNNRaaRNNwk",
            "kwNaaNNaaNwk",
            "kwRaNNNNaRwk",
            "kwagNNNNgawk",
            "kwRgNNNNgRwk",
            "kwNggNNggNwk",
            "kwNNgyygNNwk",
            "kwwwwwwwwwwk",
            "kwiiiiwwwwwk",
            "kkkkkkkkkkkk",
        ]);

        // 딥 필드 포스터 — 계단 모양 귀퉁이까지 닮은 허블 딥 필드
        Add(set, "decor_deep_field_poster", 1.0, Base,
        [
            "kkkkkkkkkkkk",
            "kcccccccccck",
            "kcNpNNNcccck",
            "kcNNyyNcccck",
            "kcsNNNwcccck",
            "kcNNaNNNpNck",
            "kcNNNNsNNNck",
            "kcwNlpNNNyck",
            "kcNNNNNaNNck",
            "kcNsNNwNNpck",
            "kcccccccccck",
            "kkkkkkkkkkkk",
        ]);
    }

    /// <summary>프레임 글자를 바꿔 끼운 새 프레임 (같은 모양, 다른 색 — 불빛 깜빡임용)</summary>
    private static string[] DecorSwap(string[] frame, params (char From, char To)[] swaps)
    {
        var copy = new string[frame.Length];
        for (var y = 0; y < frame.Length; y++)
        {
            var row = frame[y].ToCharArray();
            for (var x = 0; x < row.Length; x++)
                foreach (var (from, to) in swaps)
                    if (row[x] == from) { row[x] = to; break; }
            copy[y] = new string(row);
        }
        return copy;
    }
}
