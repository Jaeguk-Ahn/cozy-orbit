namespace CozyOrbit.Core.Game;

// 목적지 2~6: 우리 은하 너머의 실제 천체들. 각 목적지 2구역 × 6천체, 모두 Fact 가 있다.
public static partial class Destinations
{
    static partial void AddDestinations(List<DestinationDef> list)
    {
        list.Add(Andromeda);
        list.Add(Magellanic);
        list.Add(VirgoCluster);
        list.Add(DeepField);
        list.Add(CosmicWeb);
    }

    public static readonly DestinationDef Andromeda = new(
        "andromeda", "안드로메다 은하", "Andromeda Galaxy",
        "옆 동네 큰 은하로 첫 원정을 떠나요.", "Off on our first trip to the big galaxy next door.",
        new[]
        {
            new SectorDef(1, "안드로메다 원반", "Andromeda Disk"),
            new SectorDef(2, "안드로메다 중심", "Andromeda Core"),
        },
        new DiscoveryDef[]
        {
            new("ngc_206", 1, "NGC 206 별 무리", "NGC 206 Star Cloud", "파란 별들이 옹기종기 모여 있어요.", "Blue stars huddled up together.",
                "안드로메다 은하에서 가장 밝은 별 구름으로, 젊고 뜨거운 별이 많다.", "Andromeda's brightest star cloud, full of young, hot stars."),
            new("hubble_v1", 1, "허블의 변광성 V1", "Hubble's Variable V1", "두근두근 밝아졌다 어두워져요.", "Getting brighter and dimmer, ba-dum.",
                "1923년 허블이 이 별로 안드로메다가 우리 은하 밖에 있음을 밝혔다.", "In 1923 Hubble used this star to show Andromeda lies beyond our galaxy."),
            new("recurrent_nova", 1, "반복 신성", "Recurrent Nova", "해마다 펑! 하고 인사해요.", "Says hello with a pop every year.",
                "M31N 2008-12a는 약 1년마다 폭발하는, 가장 자주 터지는 신성이다.", "M31N 2008-12a erupts about once a year, the most frequent nova known."),
            new("dust_ring", 1, "먼지 고리", "Dust Ring", "은하가 먼지 목도리를 둘렀어요.", "The galaxy is wearing a dusty scarf.",
                "적외선으로 보면 안드로메다에는 별이 태어나는 큰 먼지 고리가 있다.", "In infrared light, Andromeda shows a big dusty ring where stars form."),
            new("m32", 1, "위성 은하 M32", "Satellite M32", "큰 은하 곁을 졸졸 따라다녀요.", "Tags along beside the big galaxy.",
                "안드로메다를 도는 작고 촘촘한 타원 은하다.", "A small, compact elliptical galaxy orbiting Andromeda."),
            new("m110", 1, "위성 은하 M110", "Satellite M110", "길쭉한 솜뭉치 같아요.", "Like a stretched-out cotton puff.",
                "안드로메다의 위성 은하 중 하나로, 1773년 메시에가 처음 보았다.", "One of Andromeda's satellite galaxies, first seen by Messier in 1773."),

            new("mayall_ii", 2, "메이올 II 구상성단", "Mayall II", "별들이 꼭 껴안고 동그랗게 뭉쳤어요.", "Stars hugging tight in a round ball.",
                "국부 은하군에서 가장 밝은 구상성단으로 꼽힌다.", "Counted as the brightest globular cluster in the Local Group."),
            new("giant_stream", 2, "거대 항성 흐름", "Giant Stellar Stream", "별들이 줄지어 소풍을 가요.", "Stars marching off on a picnic.",
                "안드로메다가 작은 은하를 삼키며 남은 별들의 긴 흔적이다.", "A long trail of stars left when Andromeda gobbled a small galaxy."),
            new("double_nucleus", 2, "이중 핵", "Double Nucleus", "가운데 눈이 두 개 있는 것 같아요.", "Looks like two eyes in the middle.",
                "중심에 밝은 덩어리가 두 개로 보이며, 별들의 원반 때문으로 여겨진다.", "Its center looks like two bright blobs, thought to be a disk of stars."),
            new("m31_black_hole", 2, "중심 블랙홀", "Central Black Hole", "은하 한가운데서 쿨쿨 자요.", "Snoozing at the galaxy's center.",
                "안드로메다 중심 블랙홀은 태양 수천만~1억 배 무게로 추정된다.", "Andromeda's central black hole may weigh tens of millions to 100 million Suns."),
            new("andromeda_halo", 2, "거대한 헤일로", "Vast Halo", "은하를 폭 감싸는 커다란 이불이에요.", "A giant blanket wrapped around the galaxy.",
                "가스 헤일로가 약 130만 광년까지 퍼져 우리 은하 쪽으로 닿을 듯하다.", "Its gas halo stretches about 1.3 million light-years, halfway to us."),
            new("milkomeda", 2, "밀코메다", "Milkomeda", "언젠가 우리 은하와 꼭 안을 거래요.", "Someday it'll give our galaxy a big hug.",
                "수십억 년 뒤 두 은하가 합쳐질 수도 있다. 2025년 연구는 확률을 반반쯤으로 본다.", "Our galaxy and Andromeda may merge in billions of years; a 2025 study says about 50-50."),
        });

    public static readonly DestinationDef Magellanic = new(
        "magellanic", "마젤란 은하", "Magellanic Clouds",
        "우리 은하의 귀여운 꼬마 이웃 둘이에요.", "Two cute little neighbors of our galaxy.",
        new[]
        {
            new SectorDef(1, "대마젤란 은하", "Large Magellanic Cloud"),
            new SectorDef(2, "소마젤란 은하", "Small Magellanic Cloud"),
        },
        new DiscoveryDef[]
        {
            new("tarantula_nebula", 1, "타란툴라 성운", "Tarantula Nebula", "무섭지 않은 순한 거미예요.", "A gentle spider, not scary at all.",
                "국부 은하군에서 가장 크고 밝은 별 탄생 구역이다.", "The biggest, brightest star-forming region in the Local Group."),
            new("r136a1", 1, "R136a1", "R136a1", "엄청 큰데 수줍음이 많아요.", "Super big, but very shy.",
                "알려진 가장 무거운 별로, 태양의 약 200배 무게로 추정된다.", "The most massive star known, thought to be about 200 times the Sun."),
            new("sn_1987a", 1, "초신성 1987A", "SN 1987A", "반짝 구슬 목걸이를 하고 있어요.", "Wearing a necklace of shiny beads.",
                "1987년에 터져, 망원경 시대에 가장 가까이서 본 초신성이다.", "Exploded in 1987, the closest supernova seen since telescopes were invented."),
            new("n11", 1, "N11 성운", "N11 Nebula", "분홍 솜사탕이 여기저기 피었어요.", "Pink cotton candy blooming everywhere.",
                "대마젤란 은하에서 두 번째로 큰 별 탄생 구역이다.", "The second-largest star-forming region in the Large Magellanic Cloud."),
            new("n44_superbubble", 1, "N44 초거품", "N44 Superbubble", "아주 큰 비눗방울을 불었어요.", "Someone blew a huge soap bubble.",
                "무거운 별들의 바람과 초신성이 부풀린 가스 거품이다.", "A gas bubble blown up by winds of big stars and supernovae."),
            new("lmc_bar", 1, "대마젤란 막대", "LMC Bar", "별들이 한 줄로 나란히 섰어요.", "Stars lined up in a neat row.",
                "대마젤란 은하 가운데를 가로지르는 막대 모양 별 무리다.", "A bar of stars stretched across the middle of the LMC."),

            new("ngc_346", 2, "NGC 346", "NGC 346", "분홍 구름 속 아기별 놀이터예요.", "A baby-star playground in pink clouds.",
                "소마젤란 은하에서 가장 활발한 별 탄생 구역이다.", "The most active star-forming region in the Small Magellanic Cloud."),
            new("ngc_602", 2, "NGC 602", "NGC 602", "동굴 속에 숨은 별 가족이에요.", "A star family hiding in a cave.",
                "젊은 별들이 바람으로 주변 가스에 빈 공간을 파낸 성단이다.", "A young cluster whose star winds carved a hollow in the gas."),
            new("tuc_47", 2, "47 큰부리새자리", "47 Tucanae", "반짝반짝 별 사탕 한 봉지예요.", "A bag full of sparkly star candy.",
                "맨눈으로 보이는 두 번째로 밝은 구상성단이다.", "The second-brightest globular cluster seen with the naked eye."),
            new("magellanic_bridge", 2, "마젤란 다리", "Magellanic Bridge", "두 은하가 다리를 놓았어요.", "The two galaxies built a bridge.",
                "두 마젤란 은하를 잇는 가스와 별의 다리다.", "A bridge of gas and stars linking the two Magellanic Clouds."),
            new("magellanic_stream", 2, "마젤란 흐름", "Magellanic Stream", "길고 긴 리본이 펄럭여요.", "A long, long ribbon fluttering.",
                "두 은하에서 뜯겨 나와 하늘을 길게 가로지르는 가스 띠다.", "A long ribbon of gas pulled from the two clouds across the sky."),
            new("hodge_301", 2, "호지 301", "Hodge 301", "할아버지 별들이 모여 쉬어요.", "Grandpa stars resting together.",
                "타란툴라 성운 안의 오래된 성단으로, 이미 초신성이 여럿 터졌다.", "An older cluster in the Tarantula Nebula where many stars already went supernova."),
        });

    public static readonly DestinationDef VirgoCluster = new(
        "virgo_cluster", "처녀자리 은하단", "Virgo Cluster",
        "은하 천여 개가 모인 북적북적 동네예요.", "A busy town of over a thousand galaxies.",
        new[]
        {
            new SectorDef(1, "은하단 심장", "Heart of the Cluster"),
            new SectorDef(2, "처녀자리 나선 은하", "Virgo Spirals"),
        },
        new DiscoveryDef[]
        {
            new("m87", 1, "M87 거대 타원 은하", "M87", "동글동글 커다란 찹쌀떡 같아요.", "Like a big, round rice cake.",
                "처녀자리 은하단 중심의 거대 타원 은하로, 구상성단이 1만 개가 넘는다.", "A giant elliptical at Virgo's heart with over 10,000 globular clusters."),
            new("m87_jet", 1, "M87 제트", "M87 Jet", "파란 물줄기를 쭉 뿜어요.", "Squirting a long blue stream.",
                "중심 블랙홀이 뿜는 제트로, 약 5,000광년 길이로 뻗어 있다.", "A jet shot out by the central black hole, about 5,000 light-years long."),
            new("m87_black_hole", 1, "M87* 블랙홀", "M87*", "처음으로 사진을 찍어 준 블랙홀이에요.", "The first black hole ever photographed.",
                "2019년 사건의 지평선 망원경이 찍은 최초의 블랙홀 사진 주인공이다.", "Star of the first black hole image, taken by the Event Horizon Telescope in 2019."),
            new("m86", 1, "M86", "M86", "느긋하게 은하단을 가로질러요.", "Strolling lazily across the cluster.",
                "우리 쪽으로 다가오는, 몇 안 되는 은하 중 하나다.", "One of the few galaxies moving toward us rather than away."),
            new("m84", 1, "M84", "M84", "조용하고 점잖은 타원 은하예요.", "A calm, well-mannered elliptical.",
                "마카리안 사슬 끝에 있는 렌즈형 또는 타원 은하다.", "A lenticular or elliptical galaxy at one end of Markarian's Chain."),
            new("markarian_chain", 1, "마카리안 사슬", "Markarian's Chain", "은하들이 기차놀이를 해요.", "Galaxies playing choo-choo train.",
                "하늘에서 휘어진 줄처럼 늘어선 은하 무리다.", "A row of galaxies that lines up in a curved chain on the sky."),

            new("m100", 2, "M100", "M100", "정면으로 활짝 웃는 나선 은하예요.", "A spiral galaxy smiling right at us.",
                "우리 쪽으로 얼굴을 보이는 큰 나선 은하로, 우리 은하와 비슷하다.", "A big face-on spiral galaxy, similar to our Milky Way."),
            new("m90", 2, "M90", "M90", "별 만들기를 잠시 쉬고 있어요.", "Taking a break from making stars.",
                "별 탄생이 거의 멈춘 나선 은하로, 우리 쪽으로 다가온다.", "A spiral that has almost stopped making stars, and moves toward us."),
            new("ngc_4438", 2, "눈 은하", "The Eyes", "커다란 두 눈이 깜빡깜빡해요.", "Two big eyes, blinking.",
                "NGC 4438과 NGC 4435 두 은하가 한 쌍의 눈처럼 보인다.", "NGC 4438 and NGC 4435 look like a pair of eyes together."),
            new("m60", 2, "M60", "M60", "작은 나선 친구와 나란히 있어요.", "Sitting next to a little spiral friend.",
                "거대 타원 은하로, 중심 블랙홀이 태양 약 45억 배로 추정된다.", "A giant elliptical whose black hole may weigh about 4.5 billion Suns."),
            new("m58", 2, "M58", "M58", "가운데 막대를 꼭 쥐고 돌아요.", "Spinning while holding a bar tight.",
                "처녀자리 은하단에서 가장 밝은 막대 나선 은하 중 하나다.", "One of the brightest barred spiral galaxies in the Virgo Cluster."),
            new("sombrero", 2, "솜브레로 은하", "Sombrero Galaxy", "멋진 모자를 쓰고 외출해요.", "Heading out in a fancy hat.",
                "밝은 중심과 어두운 먼지 띠가 챙 넓은 모자처럼 보인다.", "A bright core and dark dust lane make it look like a wide-brimmed hat."),
        });

    public static readonly DestinationDef DeepField = new(
        "deep_field", "먼 우주", "Deep Field",
        "모양도 가지가지, 신기한 은하 구경 가요.", "Let's go see galaxies of every funny shape.",
        new[]
        {
            new SectorDef(1, "은하 동물원", "Galaxy Zoo"),
            new SectorDef(2, "아주 먼 곳", "Deep Frontier"),
        },
        new DiscoveryDef[]
        {
            new("cartwheel_galaxy", 1, "수레바퀴 은하", "Cartwheel Galaxy", "데굴데굴 굴러갈 것 같아요.", "Looks ready to roll away.",
                "작은 은하가 가운데를 뚫고 지나가 고리 모양이 됐다.", "A smaller galaxy plunged through its middle, making a ring."),
            new("antennae_galaxies", 1, "더듬이 은하", "Antennae Galaxies", "더듬이를 쫑긋 세웠어요.", "Its antennae are perked up.",
                "두 은하가 부딪치며 길게 뻗은 꼬리가 곤충 더듬이처럼 보인다.", "Two colliding galaxies whose long tails look like insect antennae."),
            new("mice_galaxies", 1, "생쥐 은하", "Mice Galaxies", "햄찌의 먼 친척일지도 몰라요.", "Maybe distant cousins of the hamsters.",
                "서로 끌어당기는 두 은하로, 긴 꼬리가 생쥐 꼬리 같다.", "Two galaxies tugging on each other, with tails like mice."),
            new("tadpole_galaxy", 1, "올챙이 은하", "Tadpole Galaxy", "꼬리를 살랑살랑 흔들며 헤엄쳐요.", "Swimming with a wiggly tail.",
                "다른 은하와 스쳐 지나며 약 28만 광년 길이의 꼬리가 생겼다.", "A close pass with another galaxy gave it a tail about 280,000 light-years long."),
            new("hoags_object", 1, "호그의 천체", "Hoag's Object", "노른자가 동그란 우주 달걀이에요.", "A space egg with a round yolk.",
                "노란 중심을 파란 고리가 감싼 드문 고리 은하로, 1950년 발견됐다.", "A rare ring galaxy, yellow core inside a blue ring, found in 1950."),
            new("pinwheel_galaxy", 1, "바람개비 은하", "Pinwheel Galaxy", "후 불면 빙글빙글 돌 것 같아요.", "Blow on it and it might spin.",
                "M101이라고도 하며, 우리 은하보다 큰 정면 나선 은하다.", "Also called M101, a face-on spiral bigger than the Milky Way."),

            new("stephans_quintet", 2, "스테판의 오중주", "Stephan's Quintet", "다섯 은하가 합주를 해요.", "Five galaxies playing a song together.",
                "다섯 은하가 모여 보이지만, 그중 하나는 훨씬 가까이 있다.", "Five galaxies seen together, though one is much closer than the rest."),
            new("centaurus_a", 2, "센타우루스 A", "Centaurus A", "먼지 허리띠를 질끈 맸어요.", "Wearing a dusty belt, tied tight.",
                "약 1,100만 광년 거리로, 가장 가까운 활동 은하핵을 품고 있다.", "About 11 million light-years away, it holds the closest active galactic core."),
            new("bullet_cluster", 2, "총알 은하단", "Bullet Cluster", "두 은하단이 쿵 하고 부딪쳤어요.", "Two clusters went bonk!",
                "두 은하단의 충돌로, 암흑 물질이 있다는 강력한 증거를 보여 준다.", "A crash of two clusters that gives strong evidence for dark matter."),
            new("hubble_deep_field", 2, "허블 딥 필드", "Hubble Deep Field", "작은 창문에 은하가 가득해요.", "A tiny window packed with galaxies.",
                "1995년 허블이 빈 하늘 한 조각을 찍자 은하 약 3,000개가 보였다.", "In 1995 Hubble stared at a blank patch of sky and found about 3,000 galaxies."),
            new("el_gordo", 2, "엘 고르도", "El Gordo", "통통하고 듬직한 은하단이에요.", "A chubby, sturdy galaxy cluster.",
                "\"뚱보\"라는 뜻으로, 아주 먼 곳에서 발견된 가장 무거운 은하단 중 하나다.", "Means \"the fat one\": among the most massive distant clusters known."),
            new("jades_z14", 2, "JADES-GS-z14-0", "JADES-GS-z14-0", "아주아주 먼 곳의 작은 빨간 점이에요.", "A tiny red dot very, very far away.",
                "2024년 웹 망원경이 찾은 아주 먼 은하로, 빅뱅 약 3억 년 뒤의 모습이다.", "Found by Webb in 2024; we see it as it was about 300 million years after the Big Bang."),
        });

    public static readonly DestinationDef CosmicWeb = new(
        "cosmic_web", "우주 거대 구조", "Cosmic Web",
        "우주를 엮은 거대한 거미줄을 따라가요.", "Following the giant web that ties the universe.",
        new[]
        {
            new SectorDef(1, "거대 구조", "Giant Structures"),
            new SectorDef(2, "시간의 시작", "Dawn of Time"),
        },
        new DiscoveryDef[]
        {
            new("laniakea", 1, "라니아케아 초은하단", "Laniakea Supercluster", "우리 은하의 진짜 큰 동네 주소예요.", "Our galaxy's really big home address.",
                "우리 은하가 속한 초은하단으로, 하와이어로 \"끝없는 하늘\"이라는 뜻이다.", "Our home supercluster; its name means \"immeasurable heaven\" in Hawaiian."),
            new("great_attractor", 1, "거대 인력체", "Great Attractor", "모두를 살살 끌어당기는 수수께끼예요.", "A mystery gently pulling everyone in.",
                "주변 은하들이 끌려가는 곳으로, 거대한 질량이 모여 있다고 본다.", "A region galaxies drift toward; scientists think a huge mass gathers there."),
            new("bootes_void", 1, "목동자리 거대 공동", "Boötes Void", "텅 빈 곳에서 메아리가 울려요.", "Echoes ring out in the empty space.",
                "지름 약 3억 3천만 광년의, 은하가 아주 드문 거대한 빈 공간이다.", "A huge empty region about 330 million light-years wide with very few galaxies."),
            new("sloan_great_wall", 1, "슬론 장성", "Sloan Great Wall", "은하들이 쌓은 기다란 담장이에요.", "A long wall built of galaxies.",
                "2003년 슬론 디지털 전천 탐사로 찾은, 10억 광년이 넘는 은하 띠다.", "A band of galaxies over a billion light-years long, found by the Sloan survey in 2003."),
            new("hercules_wall", 1, "헤라클레스-북쪽왕관자리 장성", "Hercules–Corona Borealis Great Wall", "끝이 안 보이는 아주 긴 담장이에요.", "A wall so long you can't see the end.",
                "감마선 폭발 분포로 찾은 가장 큰 구조 후보로, 실제로 있는지는 아직 논란이다.", "Found from gamma-ray bursts; maybe the largest known structure, still debated."),
            new("cosmic_filament", 1, "우주 필라멘트", "Cosmic Filament", "은하들이 실에 꿴 구슬 같아요.", "Galaxies strung like beads on a thread.",
                "은하들은 가스와 암흑 물질의 긴 실을 따라 거미줄처럼 늘어서 있다.", "Galaxies line up along long threads of gas and dark matter, like a web."),

            new("quasar_3c273", 2, "퀘이사 3C 273", "Quasar 3C 273", "아주 먼데도 반짝 잘 보여요.", "So far away, yet shining bright.",
                "1963년에 정체가 밝혀진 최초의 퀘이사로, 우리 하늘에서 가장 밝은 퀘이사다.", "The first quasar identified (1963) and the brightest quasar in our sky."),
            new("ton_618", 2, "TON 618", "TON 618", "우주에서 손꼽히는 먹보예요.", "One of the biggest eaters in the universe.",
                "가장 무거운 블랙홀 중 하나로, 태양 약 400억 배로 추정된다.", "One of the heaviest black holes, estimated at about 40 billion Suns."),
            new("first_stars", 2, "최초의 별", "First Stars", "우주 맨 처음 켜진 불빛이에요.", "The very first lights switched on.",
                "빅뱅 수억 년 뒤 태어난 최초의 별은 아주 크고 뜨거웠을 것으로 본다.", "The first stars, born a few hundred million years after the Big Bang, were likely huge and hot."),
            new("cosmic_dawn", 2, "우주의 새벽", "Cosmic Dawn", "안개가 걷히고 아침이 밝아요.", "The fog lifts and morning comes.",
                "첫 별과 은하가 빛나며 어둡던 우주가 밝아진 시기로 여겨진다.", "The era when the first stars and galaxies lit up the dark young universe."),
            new("cmb", 2, "우주 배경 복사", "Cosmic Microwave Background", "우주의 아기 사진이에요.", "The universe's baby photo.",
                "빅뱅 약 38만 년 뒤 남은 빛으로, 1965년 우연히 발견됐다.", "Light left from about 380,000 years after the Big Bang, found by chance in 1965."),
            new("dark_energy", 2, "암흑 에너지", "Dark Energy", "보이지 않지만 우주를 쭉쭉 늘려요.", "Invisible, but stretching space out.",
                "우주 팽창을 빠르게 만드는 무언가로, 정체는 아직 수수께끼다.", "Something thought to speed up the universe's expansion; still a mystery."),
        });
}
