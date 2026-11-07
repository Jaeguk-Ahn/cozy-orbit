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
            state.Stats ??= new();
            state.Settings ??= new();
            state.Settings.DisabledProviders ??= new();
            state.Settings.Language = L.Normalize(state.Settings.Language);
            if (!double.IsFinite(state.Distance) || state.Distance < 0) state.Distance = 0;
            if (!double.IsFinite(state.Stardust) || state.Stardust < 0) state.Stardust = 0;
            if (!double.IsFinite(state.Starlight) || state.Starlight < 0) state.Starlight = 0;
            if (!double.IsFinite(state.Candy.Balance) || state.Candy.Balance < 0) state.Candy.Balance = 0;
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

    public void Save(GameState state) => JsonFile.WriteAtomic(Path, state);
}
