using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>세이브 파일 한 개에 들어가는 전부. 파생 수치(속도·용량 등)는 저장하지 않고 <see cref="GameRules"/> 가 계산한다.</summary>
public sealed class GameState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>마지막으로 시뮬레이션을 진행한 시각 (오프라인 진행의 기준)</summary>
    public DateTimeOffset LastTick { get; set; }

    /// <summary>누적 거리 (별걸음)</summary>
    public double Distance { get; set; }
    public double Stardust { get; set; }
    public double Starlight { get; set; }

    /// <summary>수동 부스트 (별빛이 바닥나면 꺼진다)</summary>
    public bool ManualBoost { get; set; }

    /// <summary>연구 id → 레벨</summary>
    public Dictionary<string, int> Upgrades { get; set; } = new();

    /// <summary>지금까지의 발견 수 (다음 발견 번호와 같다)</summary>
    public int DiscoveryCount { get; set; }
    /// <summary>도감 천체 id → 처음 만난 시각</summary>
    public Dictionary<string, DateTimeOffset> FoundAt { get; set; } = new();

    public StarlightLedgerState Ledger { get; set; } = new();
    /// <summary>별사탕 지갑 + 켜 둔 구간 (스킨 상점 화폐)</summary>
    public CandyState Candy { get; set; } = new();
    /// <summary>산 스킨 id (무료 기본 스킨은 적지 않는다)</summary>
    public HashSet<string> OwnedSkins { get; set; } = new();
    /// <summary>자리 → 장착한 스킨 id. 실제로 쓸 값은 Shop.Equipped (검증·기본값 포함)</summary>
    public Dictionary<SkinSlot, string> Equipped { get; set; } = new();
    public GameStats Stats { get; set; } = new();
    public GameSettings Settings { get; set; } = new();

    public int Level(string upgradeId) => Upgrades.TryGetValue(upgradeId, out var l) ? l : 0;

    /// <summary>새 게임. 별빛 장부는 오늘 로컬 0시부터 센다 — 설치 당일 쓴 토큰도 바로 체감되게.</summary>
    public static GameState New(DateTimeOffset now) => new()
    {
        CreatedAt = now,
        LastTick = now,
        Ledger = new StarlightLedgerState { StartedAt = LocalMidnight(now) },
        Settings = new GameSettings { Language = L.DefaultForSystem() },
    };

    public static DateTimeOffset LocalMidnight(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        return new DateTimeOffset(local.Date, local.Offset);
    }
}

/// <summary>기록용 누계 (진행에 영향 없음)</summary>
public sealed class GameStats
{
    public double StardustEarned { get; set; }
    public double StarlightEarned { get; set; }
    public double StarlightOverflow { get; set; }
    public double BoostSeconds { get; set; }
    public double PlaySeconds { get; set; }
}

public sealed class GameSettings
{
    /// <summary>"ko" 또는 "en"</summary>
    public string Language { get; set; } = "ko";
    /// <summary>별빛이 있으면 알아서 부스트 (기본 켜짐 — "토큰만큼 빨라진다"를 바로 느끼게)</summary>
    public bool AutoBoost { get; set; } = true;
    /// <summary>창 닫기 = 트레이로 숨기기</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>끈 사용량 소스 (프로바이더 id)</summary>
    public HashSet<string> DisabledProviders { get; set; } = new();
    /// <summary>플로팅 창이 떠 있었는지 — 다음 실행 때 큰 창 대신 플로팅으로 시작</summary>
    public bool FloatingOpen { get; set; }
    /// <summary>플로팅 창 위치(화면 픽셀)·크기(DIP) — 마지막 상태 기억</summary>
    public int? FloatingX { get; set; }
    public int? FloatingY { get; set; }
    public double? FloatingWidth { get; set; }
    public double? FloatingHeight { get; set; }
}
