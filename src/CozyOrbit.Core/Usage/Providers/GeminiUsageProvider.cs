using System.Text.Json;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Gemini CLI: %USERPROFILE%\.gemini\tmp\**\chats\session-*.jsonl (신형) 및 *.json (구형). 원본 spec §4.3.
/// tokens 객체가 있는 모든 객체를 집계: input = input - cached + tool, output = output + thoughts, cacheRead = cached.
/// 같은 id 의 후속 레코드(message_update 등)는 앞의 것을 대체한다.
/// </summary>
public sealed class GeminiUsageProvider : IUsageProvider
{
    public string Id => "gemini";
    public string DisplayName => "Gemini";

    private readonly FileParseCache<List<UsageEntry>> _cache = new();

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx) =>
        ScanRoots.Union(new[] { ctx.HomePath(".gemini", "tmp") }, ctx.CustomRootsRaw);

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) => Task.Run<IReadOnlyList<UsageEntry>>(() =>
    {
        var files = ResolveRoots(ctx).SelectMany(r => FileScan.Files(r, FileScan.Extension(".jsonl", ".json"), ctx.Since))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var all = new List<UsageEntry>();
        foreach (var f in files)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            all.AddRange(_cache.GetOrParse(f, ParseFile));
        }
        _cache.Retain(files);
        return Dedup.KeepMax(all).Where(e => e.Date >= ctx.Since).ToList();
    }, ctx.CancellationToken);

    internal static List<UsageEntry> ParseFile(string path)
    {
        var fileName = Path.GetFileName(path);
        var ordered = new List<string>();
        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);

        void Put(UsageEntry? e)
        {
            if (e == null) return;
            if (!byId.ContainsKey(e.Id)) ordered.Add(e.Id);
            byId[e.Id] = e; // 나중 것이 이김
        }

        if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            DateTimeOffset? lastTimestamp = null;
            foreach (var line in FileScan.ReadLines(path))
            {
                if (!line.Contains("\"tokens\"", StringComparison.Ordinal) && !line.Contains("\"timestamp\"", StringComparison.Ordinal)) continue;
                using var doc = JsonValues.TryParse(line);
                if (doc == null) continue;
                var obj = doc.RootElement;
                if (obj.Prop("timestamp") is { ValueKind: JsonValueKind.String } ts && UsageTime.ParseIso(ts.GetString()) is { } d)
                    lastTimestamp = d;
                Put(Absorb(obj, fileName, lastTimestamp));
            }
        }
        else
        {
            var text = FileScan.ReadAllText(path);
            if (text == null) return new List<UsageEntry>();
            using var doc = JsonValues.TryParse(text);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return new List<UsageEntry>();
            var root = doc.RootElement;
            DateTimeOffset? start = root.Prop("startTime") is { ValueKind: JsonValueKind.String } st ? UsageTime.ParseIso(st.GetString()) : null;
            if (root.Prop("messages") is { ValueKind: JsonValueKind.Array } msgs)
                foreach (var m in msgs.EnumerateArray()) Put(Absorb(m, fileName, start));
        }
        return ordered.Select(id => byId[id]).ToList();
    }

    private static UsageEntry? Absorb(JsonElement obj, string fileName, DateTimeOffset? fallbackDate)
    {
        var tokens = obj.Prop("tokens");
        if (!tokens.IsObject()) return null;
        DateTimeOffset? date = obj.Prop("timestamp") is { ValueKind: JsonValueKind.String } ts ? UsageTime.ParseIso(ts.GetString()) : null;
        date ??= fallbackDate;
        if (date == null) return null;
        var msgId = JsonValues.NonEmptyString(obj.Prop("id")) ?? Guid.NewGuid().ToString();
        long input = JsonValues.Int(tokens.Prop("input"));
        long cached = JsonValues.Int(tokens.Prop("cached"));
        long tool = JsonValues.Int(tokens.Prop("tool"));
        long output = JsonValues.Int(tokens.Prop("output"));
        long thoughts = JsonValues.Int(tokens.Prop("thoughts"));
        return UsageEntry.Create(
            id: $"gemini|{fileName}|{msgId}",
            date: date.Value,
            model: JsonValues.NonEmptyString(obj.Prop("model")) ?? "gemini",
            input: Math.Max(0, input - cached) + tool,
            output: output + thoughts,
            cacheWrite: 0,
            cacheRead: cached);
    }
}
