namespace CozyOrbit.Core.Usage;

public static class Dedup
{
    /// <summary>
    /// 원본 dedupKeepMax: 같은 id 중 total 이 가장 큰 레코드를 남기되 날짜는 가장 이른 것을 쓴다.
    /// (스트리밍/재개로 같은 메시지가 output 이 늘어나며 여러 번 기록되는 경우)
    /// </summary>
    public static List<UsageEntry> KeepMax(IEnumerable<UsageEntry> entries)
    {
        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!byId.TryGetValue(e.Id, out var existing)) { byId[e.Id] = e; continue; }
            var kept = e.Total > existing.Total ? e : existing;
            var earliest = e.Date < existing.Date ? e : existing;
            if (!ReferenceEquals(kept, earliest))
            {
                kept = Clone(kept);
                kept.Date = earliest.Date;
                kept.LocalDay = earliest.LocalDay;
            }
            byId[e.Id] = kept;
        }
        return byId.Values.ToList();
    }

    /// <summary>같은 id 중 가장 이른 레코드만 (Codex 최종 단계)</summary>
    public static List<UsageEntry> KeepEarliest(IEnumerable<UsageEntry> entries)
    {
        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
            if (!byId.TryGetValue(e.Id, out var existing) || e.Date < existing.Date) byId[e.Id] = e;
        return byId.Values.ToList();
    }

    /// <summary>같은 id 는 처음 것만 (Hermes)</summary>
    public static List<UsageEntry> KeepFirst(IEnumerable<UsageEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return entries.Where(e => seen.Add(e.Id)).ToList();
    }

    public static UsageEntry Clone(UsageEntry e) => new()
    {
        Id = e.Id, Date = e.Date, LocalDay = e.LocalDay, Model = e.Model,
        Input = e.Input, Output = e.Output, CacheWrite = e.CacheWrite, CacheRead = e.CacheRead,
        ExplicitCost = e.ExplicitCost, CostIsEstimate = e.CostIsEstimate, CostUnavailable = e.CostUnavailable,
    };
}
