namespace CozyOrbit.Core.Usage;

/// <summary>
/// 이 컴퓨터에 AI 도구 기록이 있는지 — 설정의 "찾음/없음" 표시와 별빛 탭의 빈 화면용.
/// 스캔 루트(<see cref="IUsageProvider.ResolveRoots"/>)가 하나라도 있는지만 본다 (내용은 읽지 않아 가볍다).
/// </summary>
public static class ProviderDetection
{
    /// <summary>프로바이더 id → 기록 폴더(또는 파일)를 찾았는지</summary>
    public static IReadOnlyDictionary<string, bool> Detect(IEnumerable<IUsageProvider> providers, ProviderContext ctx)
    {
        var found = new Dictionary<string, bool>();
        foreach (var p in providers) found[p.Id] = Found(p, ctx);
        return found;
    }

    public static bool Found(IUsageProvider provider, ProviderContext ctx)
    {
        try
        {
            return provider.ResolveRoots(ctx).Any(r => Directory.Exists(r) || File.Exists(r));
        }
        catch (Exception)
        {
            // 경로를 풀지 못하면 "없음" 으로 (스캔 자체는 따로 돈다)
            return false;
        }
    }

    /// <summary>켜 둔 소스 중 하나라도 찾았는지. 아직 찾아보기 전(found 가 비어 있음)이면 null.</summary>
    public static bool? AnyEnabledFound(IReadOnlyDictionary<string, bool> found, IEnumerable<string> providerIds, IReadOnlySet<string> disabled)
    {
        if (found.Count == 0) return null;
        return providerIds.Any(id => !disabled.Contains(id) && found.TryGetValue(id, out var f) && f);
    }
}
