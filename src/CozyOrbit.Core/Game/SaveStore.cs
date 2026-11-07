using System.Globalization;
using System.Text.Json;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.Core.Game;

/// <summary>세이브 읽기/쓰기. 쓰기는 원자적 (JsonFile). 읽을 수 없는 세이브는 지우지 않고 옆에 떼어 두고 새로 시작한다.</summary>
public sealed class SaveStore
{
    public string Path { get; }

    public SaveStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(AppPaths.StateDirectory, "save.json");
    }

    public GameState Load(DateTimeOffset now)
    {
        if (!File.Exists(Path)) return GameState.New(now);
        try
        {
            var state = JsonSerializer.Deserialize<GameState>(File.ReadAllBytes(Path), JsonFile.Options)
                        ?? throw new JsonException("empty save");
            if (state.Version > GameState.CurrentVersion)
                throw new JsonException($"save version {state.Version} is newer than {GameState.CurrentVersion}");
            state.Upgrades ??= new();
            state.FoundAt ??= new();
            state.Ledger ??= new StarlightLedgerState { StartedAt = GameState.LocalMidnight(now) };
            state.Ledger.CreditedWeighted ??= new();
            // 상점 이전 세이브: 지갑·스킨 칸이 없다
            state.Candy ??= new();
            state.Candy.Sessions ??= new();
            state.OwnedSkins ??= new();
            state.Equipped ??= new();
            // 꾸미기·목표·기록 이전 세이브
            state.OwnedDecor ??= new();
            state.PlacedDecor ??= new();
            state.Daily ??= new();
            state.Daily.Goals ??= new();
            state.Achievements ??= new();
            // 구역 완성 이전 세이브
            state.SectorsClaimed ??= new();
            state.History ??= new();
            state.Stats ??= new();
            state.Settings ??= new();
            state.Settings.DisabledProviders ??= new();
            if (state.Voyage < 0) state.Voyage = 0;
            if (state.Version < 2) MigrateToDestinations(state, now);
            state.Settings.Language = L.Normalize(state.Settings.Language);
            if (!double.IsFinite(state.Distance) || state.Distance < 0) state.Distance = 0;
            if (!double.IsFinite(state.Stardust) || state.Stardust < 0) state.Stardust = 0;
            if (!double.IsFinite(state.Starlight) || state.Starlight < 0) state.Starlight = 0;
            if (!double.IsFinite(state.Candy.Balance) || state.Candy.Balance < 0) state.Candy.Balance = 0;
            state.Settings.Volume = double.IsFinite(state.Settings.Volume) ? Math.Clamp(state.Settings.Volume, 0, 1) : 0.6;
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException)
        {
            var aside = Path + ".broken-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            AppLog.Error($"세이브를 읽지 못해 {aside} 로 옮기고 새로 시작", ex);
            try { File.Move(Path, aside, overwrite: true); } catch (IOException) { }
            return GameState.New(now);
        }
    }

    /// <summary>
    /// v1 → v2: 우리 은하 도감에 실제 천체 구역이 끼어들어 순서가 바뀌었다. 발견 수는 그대로 두고,
    /// 도감은 새 순서의 앞에서부터 발견 수만큼을 만난 것으로 다시 적는다 (원래 시각이 있으면 그 시각).
    /// 누계 기록이 없던 값은 지금 상태에서 채운다.
    /// </summary>
    private static void MigrateToDestinations(GameState state, DateTimeOffset now)
    {
        var old = state.FoundAt;
        var fallback = old.Count > 0 ? old.Values.Min() : now;
        state.FoundAt = Destinations.MilkyWay.Catalog.Take(state.DiscoveryCount)
            .ToDictionary(d => d.Id, d => old.TryGetValue(d.Id, out var t) ? t : fallback);
        state.Stats.Discoveries = Math.Max(state.Stats.Discoveries, state.DiscoveryCount);
        state.Stats.DistanceTotal = Math.Max(state.Stats.DistanceTotal, state.Distance);
        state.Stats.ResearchBought = Math.Max(state.Stats.ResearchBought, state.Upgrades.Values.Sum());
        state.Version = 2;
    }

    public void Save(GameState state) => JsonFile.WriteAtomic(Path, state);
}
