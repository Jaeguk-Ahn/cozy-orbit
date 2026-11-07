namespace CozyOrbit.Core.Art;

/// <summary>
/// 게임 전체가 쓰는 파스텔 제한 팔레트 (0xAARRGGBB, 직선 알파). 스프라이트와 UI 가 같은 색만 써서 톤이 흩어지지 않게 한다.
/// 외곽선은 검정이 아니라 짙은 보라갈색, 우주 배경은 짙은 남보라.
/// 스킨·테마가 더하는 색은 Palette.&lt;영역&gt;.cs partial 파일에 둔다.
/// </summary>
public static partial class Palette
{
    // ── 선 ──
    /// <summary>외곽선 — 짙은 보라갈색</summary>
    public const uint Outline = 0xFF4A3048;
    /// <summary>안쪽 선·짙은 그림자 — 외곽선보다 한 단계 밝은 보라갈색</summary>
    public const uint OutlineSoft = 0xFF7A5470;

    // ── 밝은 색 ──
    public const uint White = 0xFFFFF8EC;
    public const uint Cream = 0xFFFBE3C0;
    public const uint Butter = 0xFFFFE28A;
    public const uint Gold = 0xFFF5BE5C;
    public const uint Apricot = 0xFFF09A62;

    // ── 갈색 (도토리·흙·햄찌) ──
    public const uint Tan = 0xFFD9A06A;
    public const uint Brown = 0xFFA8694A;
    public const uint BrownDark = 0xFF74463A;

    // ── 분홍·보라 ──
    public const uint Pink = 0xFFF7AFC4;
    public const uint Rose = 0xFFE0789A;
    public const uint Berry = 0xFFE5636E;
    public const uint Lilac = 0xFFC9AEEA;
    public const uint Purple = 0xFF9077C8;

    // ── 파랑·초록 ──
    public const uint Sky = 0xFFAEDCF2;
    public const uint Blue = 0xFF6FA2DC;
    public const uint Mint = 0xFFA4E8C6;
    public const uint Teal = 0xFF5DB4A4;

    // ── 회색 (보라 기운) ──
    public const uint GrayLight = 0xFFDAD5E6;
    public const uint Gray = 0xFFA59EBA;
    public const uint GrayDark = 0xFF6E6888;

    // ── UI ──
    /// <summary>장면 배경 (우주)</summary>
    public const uint Space = 0xFF201B3E;
    /// <summary>배경 그라데이션의 더 어두운 쪽</summary>
    public const uint SpaceDeep = 0xFF13102A;
    /// <summary>UI 카드 배경</summary>
    public const uint Panel = 0xFF2E2754;
    public const uint PanelLight = 0xFF41386E;
    public const uint PanelBorder = 0xFF6C5C9E;
    public const uint Text = 0xFFF6EEDF;
    public const uint TextDim = 0xFFAA9DC9;
    /// <summary>별빛 (에너지) — 노랑</summary>
    public const uint Starlight = Butter;
    /// <summary>별가루 (화폐) — 분홍빛 라일락</summary>
    public const uint Stardust = 0xFFF0A8DC;
    /// <summary>좋음·완료 — 민트</summary>
    public const uint Good = Mint;
    /// <summary>주의 — 복숭아</summary>
    public const uint Warn = 0xFFFFB98A;
}
