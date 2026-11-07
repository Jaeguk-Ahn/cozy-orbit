using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>연구 하나의 정의 (docs/design.md §5). 효과 계산은 <see cref="GameRules"/>.</summary>
public sealed class UpgradeDef
{
    public required string Id { get; init; }
    public required string Icon { get; init; }
    public required string NameKo { get; init; }
    public required string NameEn { get; init; }
    public required double BaseCost { get; init; }
    public required double Growth { get; init; }
    /// <summary>최대 레벨 (null = 끝없음)</summary>
    public int? MaxLevel { get; init; }
    /// <summary>효과 이름 ("기본 속도")</summary>
    public required string EffectKo { get; init; }
    public required string EffectEn { get; init; }
    /// <summary>레벨 → 효과 값 ("×1.21", 현재 언어)</summary>
    public required Func<int, string> Value { get; init; }

    public string Name => L.T(NameKo, NameEn);
    public string Effect => L.T(EffectKo, EffectEn);

    /// <summary>level → level+1 비용 (내림 정수)</summary>
    public double CostAt(int level) => Math.Floor(BaseCost * Math.Pow(Growth, level));

    public bool IsMaxed(int level) => MaxLevel is int max && level >= max;
}

public static class Upgrades
{
    public const string Engine = "engine";
    public const string Battery = "battery";
    public const string Sail = "sail";
    public const string Booster = "booster";
    public const string Scope = "scope";
    public const string Crew = "crew";
    public const string Bunk = "bunk";

    public static readonly IReadOnlyList<UpgradeDef> All = new[]
    {
        new UpgradeDef
        {
            Id = Engine, Icon = "icon_engine", NameKo = "도토리 엔진", NameEn = "Acorn Engine",
            BaseCost = 50, Growth = 1.6,
            EffectKo = "기본 속도", EffectEn = "Base speed",
            Value = l => "×" + Fmt.Number(GameRules.EngineMultiplier(l), 2),
        },
        new UpgradeDef
        {
            Id = Battery, Icon = "icon_battery", NameKo = "별빛 배터리", NameEn = "Starlight Battery",
            BaseCost = 80, Growth = 1.7,
            EffectKo = "용량", EffectEn = "Capacity",
            Value = l => Fmt.Duration(GameRules.BatteryCapacity(l)),
        },
        new UpgradeDef
        {
            Id = Sail, Icon = "icon_sail", NameKo = "태양 돛", NameEn = "Solar Sail",
            BaseCost = 120, Growth = 1.8, MaxLevel = 10,
            EffectKo = "별빛 충전", EffectEn = "Starlight charge",
            Value = l => L.T($"+{Fmt.Duration(GameRules.SailPerSecond(l) * 3600)}/시간", $"+{Fmt.Duration(GameRules.SailPerSecond(l) * 3600)}/h"),
        },
        new UpgradeDef
        {
            Id = Booster, Icon = "icon_booster", NameKo = "반짝 부스터", NameEn = "Twinkle Booster",
            BaseCost = 150, Growth = 1.9, MaxLevel = 10,
            EffectKo = "부스트", EffectEn = "Boost",
            Value = l => "×" + Fmt.Number(GameRules.BoostMultiplier(l), 1),
        },
        new UpgradeDef
        {
            Id = Scope, Icon = "icon_scope", NameKo = "별가루 망원경", NameEn = "Stardust Scope",
            BaseCost = 60, Growth = 1.65,
            EffectKo = "별가루", EffectEn = "Stardust",
            Value = l => "×" + Fmt.Number(GameRules.DustMultiplier(l), 2),
        },
        new UpgradeDef
        {
            Id = Crew, Icon = "icon_crew", NameKo = "햄찌 크루", NameEn = "Hamster Crew",
            BaseCost = 200, Growth = 2.2, MaxLevel = 6,
            EffectKo = "크루", EffectEn = "Crew",
            Value = l => L.T($"{l}명 (+{Fmt.Number(l * Balance.DustPerCrewPerSecond, 1)}/초)", $"{l} (+{Fmt.Number(l * Balance.DustPerCrewPerSecond, 1)}/s)"),
        },
        new UpgradeDef
        {
            Id = Bunk, Icon = "icon_bunk", NameKo = "포근한 침낭", NameEn = "Cozy Sleeping Bag",
            BaseCost = 100, Growth = 2.0, MaxLevel = 8,
            EffectKo = "자리 비움 진행", EffectEn = "Away progress",
            Value = l => Fmt.Duration(GameRules.OfflineCap(l).TotalSeconds),
        },
    };

    public static UpgradeDef Get(string id) => All.First(u => u.Id == id);
}
