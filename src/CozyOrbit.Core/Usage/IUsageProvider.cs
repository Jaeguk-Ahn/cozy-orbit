namespace CozyOrbit.Core.Usage;

/// <summary>프로바이더 로드에 필요한 공통 문맥.</summary>
public sealed class ProviderContext
{
    public required DateTimeOffset Now { get; init; }
    /// <summary>스캔 하한 (UsageTime.EnrichmentScanStart). 파일 mtime / SQL 시간 하한으로 사용.</summary>
    public required DateTimeOffset Since { get; init; }
    /// <summary>환경 변수 조회 (비어 있거나 공백이면 null 을 돌려줘야 함)</summary>
    public Func<string, string?> Env { get; init; } = DefaultEnv;
    /// <summary>설정 → 추가 스캔 폴더 (이 프로바이더 것, 원문 문자열)</summary>
    public string? CustomRootsRaw { get; init; }
    public HttpClient? Http { get; init; }
    public CancellationToken CancellationToken { get; init; }
    /// <summary>사용자 홈 (테스트에서 바꿀 수 있게)</summary>
    public string Home { get; init; } = Infra.AppPaths.Home;
    public string RoamingAppData { get; init; } = Infra.AppPaths.RoamingAppData;
    public string LocalAppData { get; init; } = Infra.AppPaths.LocalAppData;

    public static string? DefaultEnv(string name)
    {
        var v = Environment.GetEnvironmentVariable(name)
                ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    public string HomePath(params string[] parts) => Path.Combine(new[] { Home }.Concat(parts).ToArray());
}

/// <summary>
/// 로컬 AI 도구 하나의 사용량 소스. 범용 집계 경로에 특정 프로바이더 분기를 넣지 않는다(원본 확장 규약).
/// </summary>
public interface IUsageProvider
{
    /// <summary>원본과 같은 id (claude_code, codex, gemini, ...)</summary>
    string Id { get; }
    string DisplayName { get; }
    /// <summary>오늘 사용량을 모델별로 풀어 보여 줄지 (Pi, omp, Kimi)</summary>
    bool IncludeModels => false;
    /// <summary>설정 화면 표시·진단용: 이 문맥에서 실제로 스캔할 루트(파일 또는 폴더)</summary>
    IReadOnlyList<string> ResolveRoots(ProviderContext ctx);
    /// <summary>ctx.Since 이후의 레코드. 실패는 예외로 알린다 (이전 결과 유지는 호출자가 결정).</summary>
    Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx);
}
