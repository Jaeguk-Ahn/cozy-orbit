using System.Text;
using System.Text.Json;

namespace CozyOrbit.Core.Usage.Providers;

/// <summary>
/// Codex CLI: %USERPROFILE%\.codex\{sessions,archived_sessions}\**\rollout-*.jsonl (+ %CODEX_HOME%). 원본 spec §4.2.
/// token_count 이벤트의 last_token_usage(턴 델타)를 집계한다.
/// fork/subagent 파일은 부모 기록을 앞부분에 다시 써 넣으므로(replay) 부모 rollout 과 usage-state 벡터를
/// 대조해 복사된 prefix 를 버린다. 부모를 못 찾으면 시간(1초) 기반 fallback.
/// 최종 id = "codex|소유 세션|epoch|누적벡터|last벡터" → 여러 파일에 같은 상태가 남아도 한 번만 센다.
/// </summary>
public sealed class CodexUsageProvider : IUsageProvider
{
    public string Id => "codex";
    public string DisplayName => "Codex";

    /// <summary>fork replay 는 ms 단위로 기록된다 — 이 간격 미만이면 replay 로 본다.</summary>
    private const double ForkReplayMaximumGapSeconds = 1;
    /// <summary>부모 탐색용 첫 session_meta probe 의 최대 읽기 바이트</summary>
    internal const int ProbeByteLimit = 1 << 20;
    private const int ProbeChunkSize = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // 파일별 파싱 결과 (비싼 부분). 해석(resolve)은 매 로드마다 파싱 결과 위에서 다시 수행.
    private readonly FileParseCache<ParsedRollout> _parseCache = new();
    // 파일을 열지 않고 아는 세션 id (파싱 또는 probe 결과). (mtime, size) 가 같을 때만 유효.
    private readonly Dictionary<string, (DateTime Mtime, long Size, string? SessionId)> _sessionIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexGate = new();

    public IReadOnlyList<string> ResolveRoots(ProviderContext ctx)
    {
        var defaults = new List<string>
        {
            ctx.HomePath(".codex", "sessions"),
            ctx.HomePath(".codex", "archived_sessions"),
        };
        // 원본 앱은 CODEX_HOME 을 보지 않지만 Codex 자체는 따르므로 추가(기본값에 더함).
        var codexHome = ctx.Env("CODEX_HOME")?.Trim();
        if (!string.IsNullOrEmpty(codexHome))
        {
            defaults.Add(Path.Combine(codexHome, "sessions"));
            defaults.Add(Path.Combine(codexHome, "archived_sessions"));
        }
        return ScanRoots.Union(defaults, ctx.CustomRootsRaw);
    }

    public Task<IReadOnlyList<UsageEntry>> LoadAsync(ProviderContext ctx) =>
        Task.Run<IReadOnlyList<UsageEntry>>(() => Load(ctx), ctx.CancellationToken);

    private List<UsageEntry> Load(ProviderContext ctx)
    {
        var ct = ctx.CancellationToken;
        var files = RolloutFiles(ResolveRoots(ctx));
        var window = files.Where(f => f.Mtime >= ctx.Since).ToList();

        // 이번 호출에서 읽기에 실패한 파일 — 인덱스에 남기지 않고(다음에 재시도) 같은 파일 반복 열기만 막는다.
        var failedProbes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ParsedRollout LoadFile(RolloutFile f)
        {
            ct.ThrowIfCancellationRequested();
            var rollout = _parseCache.GetOrParse(f.FilePath, ParseRollout);
            Remember(f, rollout.SessionId);
            return rollout;
        }

        string? ProbeFile(RolloutFile f)
        {
            ct.ThrowIfCancellationRequested();
            if (failedProbes.Contains(f.FilePath)) return null;
            var (ok, id) = ProbeSessionId(f.FilePath);
            if (!ok) { failedProbes.Add(f.FilePath); return null; }
            Remember(f, id); // null = "이 파일엔 세션 id 가 없다"(확정)
            return id;
        }

        var (rollouts, included) = ExpandParentClosure(window, files, LoadFile, Knowledge, ProbeFile);
        var entries = ResolveRollouts(rollouts, included);

        // 사라진 파일의 캐시 항목 정리
        var live = files.Select(f => f.FilePath).ToList();
        _parseCache.Retain(live);
        var liveSet = new HashSet<string>(live, StringComparer.OrdinalIgnoreCase);
        lock (_indexGate)
            foreach (var stale in _sessionIndex.Keys.Where(p => !liveSet.Contains(p)).ToList()) _sessionIndex.Remove(stale);

        return entries.Where(e => e.Date >= ctx.Since).ToList();
    }

    private void Remember(RolloutFile f, string? sessionId)
    {
        lock (_indexGate) _sessionIndex[f.FilePath] = (f.Mtime.UtcDateTime, f.Size, sessionId);
    }

    /// <summary>파일을 열지 않고 아는 세션 id. known=false 면 모름.</summary>
    private (bool Known, string? SessionId) Knowledge(RolloutFile f)
    {
        lock (_indexGate)
        {
            if (_sessionIndex.TryGetValue(f.FilePath, out var k) && k.Mtime == f.Mtime.UtcDateTime && k.Size == f.Size)
                return (true, k.SessionId);
        }
        return (false, null);
    }

    // ------------------------------------------------------------------ 파일 목록

    internal readonly record struct RolloutFile(string FilePath, DateTimeOffset Mtime, long Size);

    /// <summary>루트들의 *.jsonl 전체 (조회 윈도우 밖 파일도 부모 후보라 거르지 않음), 경로순.</summary>
    internal static List<RolloutFile> RolloutFiles(IEnumerable<string> roots)
    {
        var list = new List<RolloutFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var path in FileScan.Files(root, FileScan.Extension(".jsonl")))
            {
                if (!seen.Add(path)) continue;
                try
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists) continue;
                    list.Add(new RolloutFile(path, new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero), fi.Length));
                }
                catch (Exception) { /* 접근 불가 파일은 건너뜀 */ }
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.FilePath, b.FilePath));
        return list;
    }

    // ------------------------------------------------------------------ 파싱 모델

    internal readonly record struct UsageVector(long Input, long CachedInput, long CacheWriteInput, long Output, long ReasoningOutput, long Total)
    {
        /// <summary>intOrNil ?? 0 (거대 값은 1e18 로 클램프)</summary>
        public static UsageVector From(JsonElement raw) => new(
            JsonValues.IntOrNull(raw.Prop("input_tokens")) ?? 0,
            JsonValues.IntOrNull(raw.Prop("cached_input_tokens")) ?? 0,
            JsonValues.IntOrNull(raw.Prop("cache_write_input_tokens")) ?? 0,
            JsonValues.IntOrNull(raw.Prop("output_tokens")) ?? 0,
            JsonValues.IntOrNull(raw.Prop("reasoning_output_tokens")) ?? 0,
            JsonValues.IntOrNull(raw.Prop("total_tokens")) ?? 0);

        public string Fingerprint =>
            FormattableString.Invariant($"{Input},{CachedInput},{CacheWriteInput},{Output},{ReasoningOutput},{Total}");

        /// <summary>parseCodexLine 매핑과 같은 과금 구성 합</summary>
        public long Billable => Math.Max(0, Input - CachedInput) + CachedInput + Output;

        public bool IsLowerThan(UsageVector p) =>
            Input < p.Input || CachedInput < p.CachedInput || CacheWriteInput < p.CacheWriteInput
            || Output < p.Output || ReasoningOutput < p.ReasoningOutput || Total < p.Total;
    }

    /// <summary>값 비교(record) — 12개 숫자가 모두 같으면 같은 상태</summary>
    internal sealed record UsageState(UsageVector Cumulative, UsageVector Last)
    {
        public string Fingerprint => Cumulative.Fingerprint + "|" + Last.Fingerprint;
    }

    internal sealed record CodexEvent(UsageEntry Entry, UsageState? State, string? SessionId);

    internal sealed class ParsedRollout
    {
        public required string FilePath { get; init; }
        public string? SessionId { get; init; }
        public string? ParentSessionId { get; init; }
        public DateTimeOffset? ForkedAt { get; init; }
        public bool IsSubagent { get; init; }
        public required IReadOnlyList<CodexEvent> Events { get; init; }
    }

    private readonly record struct SessionMeta(string? Id, string? ParentId, DateTimeOffset? Date, bool IsSubagent);

    private sealed record ResolvedEvent(UsageEntry Entry, UsageState? State);

    private sealed record ResolvedRollout(List<ResolvedEvent> History, List<UsageEntry> Owned);

    // ------------------------------------------------------------------ 파일 파싱

    /// <summary>파일 내부 정보만 파싱 (fork replay 판정은 다른 rollout 과 대조해 나중에).</summary>
    internal static ParsedRollout ParseRollout(string path)
    {
        var fileName = Path.GetFileName(path);
        var events = new List<CodexEvent>();
        int turn = 0;
        string? sessionId = null, parentSessionId = null;
        DateTimeOffset? forkedAt = null;
        bool isSubagent = false;
        string? currentSessionId = null;
        string? previousStateSession = null;
        UsageState? previousState = null;
        string model = "codex"; // turn_context 가 나오기 전까지는 가격표에 없는 모델

        foreach (var line in FileScan.ReadLines(path))
        {
            // 대부분의 줄은 response_item/delta — 필요한 세 종류만 JSON 파싱
            bool hasMeta = line.Contains("session_meta", StringComparison.Ordinal);
            bool hasModel = line.Contains("\"model\"", StringComparison.Ordinal);
            bool hasToken = line.Contains("token_count", StringComparison.Ordinal);
            if (!hasMeta && !hasModel && !hasToken) continue;

            using var doc = JsonValues.TryParse(line);
            if (doc == null) continue;
            var obj = doc.RootElement;

            if (hasMeta && ParseMeta(obj) is { } meta)
            {
                if (sessionId == null)
                {
                    // subagent meta 는 id 가 child, session_id 가 parent 일 수 있어 id 우선
                    sessionId = meta.Id;
                    parentSessionId = meta.ParentId;
                    forkedAt = meta.Date;
                    isSubagent = meta.IsSubagent;
                }
                if (meta.Id != null && meta.Id != currentSessionId)
                {
                    // fork 파일은 자기 meta 뒤에 부모 meta 를 다시 넣는다 → 세션 전환
                    currentSessionId = meta.Id;
                    previousStateSession = null;
                    previousState = null;
                }
            }

            if (hasModel && ModelOf(obj) is { } m) model = m;

            if (!hasToken) continue;
            var parsed = ParseTokenLine(obj, fileName, turn, model);
            if (parsed == null) continue;
            turn++; // 버려지는 동일 snapshot 도 턴 번호는 소비 (원본 defer 와 동일)

            var state = parsed.Value.State;
            if (state != null && currentSessionId != null)
            {
                // Codex 는 같은 cumulative/last 상태를 그대로 다시 기록할 수 있다 → 한 번만
                if (previousState != null && previousStateSession == currentSessionId && previousState == state) continue;
                previousStateSession = currentSessionId;
                previousState = state;
            }
            else
            {
                previousStateSession = null;
                previousState = null;
            }
            events.Add(new CodexEvent(parsed.Value.Entry, state, currentSessionId));
        }

        return new ParsedRollout
        {
            FilePath = path,
            SessionId = sessionId,
            ParentSessionId = parentSessionId,
            ForkedAt = forkedAt,
            IsSubagent = isSubagent,
            Events = events,
        };
    }

    /// <summary>비어 있지 않은 문자열 (원본은 trim 하지 않음)</summary>
    private static string? RawNonEmpty(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v && v.GetString() is { Length: > 0 } s ? s : null;

    private static SessionMeta? ParseMeta(JsonElement obj)
    {
        if (obj.Prop("type") is not { ValueKind: JsonValueKind.String } type || type.GetString() != "session_meta") return null;
        if (obj.Prop("payload") is not { ValueKind: JsonValueKind.Object } payload) return null;
        var id = RawNonEmpty(payload.Prop("id")) ?? RawNonEmpty(payload.Prop("session_id"));
        var parentId = RawNonEmpty(payload.Prop("forked_from_id")) ?? RawNonEmpty(payload.Prop("parent_thread_id"));
        DateTimeOffset? date = obj.Prop("timestamp") is { ValueKind: JsonValueKind.String } ts ? UsageTime.ParseIso(ts.GetString()) : null;
        bool isSubagent = (payload.Prop("thread_source") is { ValueKind: JsonValueKind.String } src && src.GetString() == "subagent")
                          || (payload.Prop("source") is { ValueKind: JsonValueKind.Object } source && source.TryGetProperty("subagent", out _));
        return new SessionMeta(id, parentId, date, isSubagent);
    }

    /// <summary>payload.model 또는 payload.turn_context.model (문자열)</summary>
    private static string? ModelOf(JsonElement obj)
    {
        if (obj.Prop("payload") is not { ValueKind: JsonValueKind.Object } payload) return null;
        if (payload.Prop("model") is { ValueKind: JsonValueKind.String } m) return m.GetString();
        if (payload.Prop("turn_context") is { ValueKind: JsonValueKind.Object } tc
            && tc.Prop("model") is { ValueKind: JsonValueKind.String } tm) return tm.GetString();
        return null;
    }

    /// <summary>
    /// token_count 한 줄 → (임시 id 레코드, usage state).
    /// input = input_tokens − cached, cacheRead = cached, output = output_tokens(reasoning 포함), cacheWrite = 0.
    /// #278: 구성값이 모두 0 이고 total 만 있으면 세션이 total-only 이거나 last.total == cumulative.total 일 때만 input 에 넣는다.
    /// </summary>
    private static (UsageEntry Entry, UsageState? State)? ParseTokenLine(JsonElement obj, string fileName, int turn, string model)
    {
        if (obj.Prop("payload") is not { ValueKind: JsonValueKind.Object } payload) return null;
        if (payload.Prop("type") is not { ValueKind: JsonValueKind.String } pt || pt.GetString() != "token_count") return null;
        if (payload.Prop("info") is not { ValueKind: JsonValueKind.Object } info) return null; // rate-limit 전용 이벤트
        if (info.Prop("last_token_usage") is not { ValueKind: JsonValueKind.Object } last) return null;
        if (obj.Prop("timestamp") is not { ValueKind: JsonValueKind.String } ts) return null;
        if (UsageTime.ParseIso(ts.GetString()) is not { } date) return null;

        long inputTotal = JsonValues.Int(last.Prop("input_tokens"));
        long cached = JsonValues.Int(last.Prop("cached_input_tokens"));
        long output = JsonValues.Int(last.Prop("output_tokens"));
        long lastTotal = JsonValues.Int(last.Prop("total_tokens"));
        long nonCachedInput = Math.Max(0, inputTotal - cached);

        UsageVector? cumulative = null;
        if (info.Prop("total_token_usage") is { ValueKind: JsonValueKind.Object } tot) cumulative = UsageVector.From(tot);

        bool componentsZero = nonCachedInput + cached + output == 0;
        long input, outTokens, cacheRead;
        if (componentsZero && lastTotal > 0 && ShouldTrustTotalOnly(lastTotal, cumulative))
        {
            input = lastTotal; outTokens = 0; cacheRead = 0;
        }
        else
        {
            input = nonCachedInput; outTokens = output; cacheRead = cached;
        }

        var entry = UsageEntry.Create(
            FormattableString.Invariant($"codex|{fileName}|{turn}"), date, model,
            input, outTokens, 0, cacheRead,
            costUnavailable: componentsZero && lastTotal > 0);
        UsageState? state = cumulative is { } c ? new UsageState(c, UsageVector.From(last)) : null;
        return (entry, state);
    }

    /// <summary>
    /// 구성값이 0 인 last 의 total 을 믿을지: 누적값 없음 → 믿음 / 누적도 구성 0 인데 total 만 있음 → 믿음 /
    /// last.total == cumulative.total (세션 전체) → 믿음 / 그 외(fork replay 직후 zero-context 턴) → 0 유지.
    /// </summary>
    private static bool ShouldTrustTotalOnly(long lastTotal, UsageVector? cumulative)
    {
        if (cumulative is not { } cum) return true;
        if (cum.Billable == 0 && cum.Total > 0) return true;
        return cum.Total == lastTotal;
    }

    // ------------------------------------------------------------------ 부모 probe

    private enum ProbeKind { SessionId, Stop, Invalid, KeepScanning }

    /// <summary>
    /// 부모 후보 파일의 첫 session_meta id 만 읽는다 (최대 byteLimit 바이트). 완성된 줄 단위로만 UTF-8 디코드.
    /// token_count 를 먼저 만나거나 UTF-8 이 아닌 줄이면 중단(null). Ok=false 는 파일을 열거나 읽지 못한 경우.
    /// </summary>
    internal static (bool Ok, string? SessionId) ProbeSessionId(string path, int byteLimit = ProbeByteLimit)
    {
        FileStream fs;
        try { fs = FileScan.OpenShared(path); }
        catch (Exception) { return (false, null); }
        using (fs)
        {
            try
            {
                var data = new byte[Math.Max(0, byteLimit)];
                int read = 0, lineStart = 0;
                while (read < byteLimit)
                {
                    int n = fs.Read(data, read, Math.Min(ProbeChunkSize, byteLimit - read));
                    if (n <= 0)
                    {
                        // EOF — 개행 없이 끝나는 마지막 줄도 완성된 줄로 취급
                        var tail = ProbeOutcome(data, lineStart, read - lineStart);
                        return (true, tail.Kind == ProbeKind.SessionId ? tail.Id : null);
                    }
                    int scanFrom = read;
                    read += n;
                    for (int i = scanFrom; i < read; i++)
                    {
                        if (data[i] != (byte)'\n') continue;
                        var outcome = ProbeOutcome(data, lineStart, i - lineStart);
                        switch (outcome.Kind)
                        {
                            case ProbeKind.SessionId: return (true, outcome.Id);
                            case ProbeKind.Stop:
                            case ProbeKind.Invalid: return (true, null);
                        }
                        lineStart = i + 1;
                    }
                }
                // 상한 도달 — 상한에서 정확히 끝난 완성 줄이면 인정
                var last = ProbeOutcome(data, lineStart, read - lineStart);
                return (true, last.Kind == ProbeKind.SessionId ? last.Id : null);
            }
            catch (IOException) { return (false, null); }
            catch (UnauthorizedAccessException) { return (false, null); }
        }
    }

    private static (ProbeKind Kind, string? Id) ProbeOutcome(byte[] data, int start, int length)
    {
        if (length <= 0) return (ProbeKind.KeepScanning, null);
        string line;
        try { line = StrictUtf8.GetString(data, start, length); }
        catch (DecoderFallbackException) { return (ProbeKind.Invalid, null); } // 손상된 줄 뒤의 부모 meta 를 오인하지 않게 중단
        if (line.Contains("session_meta", StringComparison.Ordinal))
        {
            using var doc = JsonValues.TryParse(line);
            if (doc != null && ParseMeta(doc.RootElement) is { } meta) return (ProbeKind.SessionId, meta.Id);
        }
        if (line.Contains("token_count", StringComparison.Ordinal)) return (ProbeKind.Stop, null);
        return (ProbeKind.KeepScanning, null);
    }

    /// <summary>파일명 부분일치 힌트로 쓸 수 있는 id 인가 (퇴화된 값은 모든 파일에 걸려 전부 full-parse 하게 됨)</summary>
    internal static bool IsUsableFilenameHint(string id) => id.Length >= 4 && id.Any(char.IsLetterOrDigit);

    // ------------------------------------------------------------------ 부모 closure

    /// <summary>
    /// 윈도우 안 rollout 에서 시작해 replay 대조에 필요한 부모(조부모까지)를 끌어온다.
    /// 힌트(아는 세션 id / 파일명)로 후보를 고르고, 채택은 실제 파싱된 세션 id 로만 판정.
    /// </summary>
    private static (List<ParsedRollout> Rollouts, HashSet<string> Included) ExpandParentClosure(
        List<RolloutFile> windowFiles,
        List<RolloutFile> allFiles,
        Func<RolloutFile, ParsedRollout> load,
        Func<RolloutFile, (bool Known, string? SessionId)> knowledge,
        Func<RolloutFile, string?> probe)
    {
        var rolloutsByPath = new Dictionary<string, ParsedRollout>(StringComparer.Ordinal);
        foreach (var f in windowFiles)
        {
            var r = load(f);
            rolloutsByPath[r.FilePath] = r;
        }
        var included = new HashSet<string>(windowFiles.Select(f => f.FilePath), StringComparer.Ordinal);

        var pending = new HashSet<string>(
            rolloutsByPath.Values.Where(r => r.ParentSessionId != null).Select(r => r.ParentSessionId!), StringComparer.Ordinal);
        var searched = new HashSet<string>(StringComparer.Ordinal);

        bool Adopt(List<RolloutFile> candidates, string parentId)
        {
            bool resolved = false;
            foreach (var candidate in candidates)
            {
                var parent = load(candidate);
                if (parent.SessionId != parentId) continue;
                rolloutsByPath[parent.FilePath] = parent;
                resolved = true;
                if (parent.ParentSessionId is { } ancestorId) pending.Add(ancestorId);
            }
            return resolved;
        }

        while (true)
        {
            var parentId = pending.FirstOrDefault(p => !searched.Contains(p));
            if (parentId == null) break;
            searched.Add(parentId);
            if (rolloutsByPath.Values.Any(r => r.SessionId == parentId)) continue;

            var unresolved = allFiles.Where(f => !rolloutsByPath.ContainsKey(f.FilePath)).ToList();
            // 파일을 열지 않고 후보를 좁힌다: 아는 id 는 그 값으로만, 모르면 파일명 힌트
            var hinted = unresolved.Where(f =>
            {
                var (known, id) = knowledge(f);
                if (known) return id == parentId;
                return IsUsableFilenameHint(parentId) && Path.GetFileName(f.FilePath).Contains(parentId, StringComparison.Ordinal);
            }).ToList();
            if (Adopt(hinted, parentId)) continue;

            // 힌트가 없거나 전부 실패 → 세션 id 를 모르는 파일만 probe
            var hintedPaths = new HashSet<string>(hinted.Select(f => f.FilePath), StringComparer.Ordinal);
            var probed = unresolved.Where(f => !hintedPaths.Contains(f.FilePath) && !knowledge(f).Known && probe(f) == parentId).ToList();
            Adopt(probed, parentId);
        }
        return (rolloutsByPath.Values.ToList(), included);
    }

    // ------------------------------------------------------------------ fork/subagent 해석

    /// <summary>
    /// 부모 관계가 확인된 rollout 끼리 usage-state prefix 를 대조해 실제로 복사된 replay 만 제거.
    /// 부모를 못 찾은 manual fork 는 1초 trimming fallback, subagent 는 모두 보존.
    /// </summary>
    internal static List<UsageEntry> ResolveRollouts(IReadOnlyList<ParsedRollout> rollouts, IEnumerable<string> includedPaths)
    {
        var bySession = rollouts.Where(r => r.SessionId != null)
            .GroupBy(r => r.SessionId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FilePath, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var byPath = new Dictionary<string, ParsedRollout>(StringComparer.Ordinal);
        foreach (var r in rollouts) byPath[r.FilePath] = r;
        var memo = new Dictionary<string, ResolvedRollout>(StringComparer.Ordinal);

        ResolvedRollout Resolve(ParsedRollout rollout, HashSet<string> visiting)
        {
            if (memo.TryGetValue(rollout.FilePath, out var cached)) return cached;
            if (!visiting.Add(rollout.FilePath))
                return ResolveOwnedEvents(rollout, FallbackReplayCount(rollout), null); // 순환
            try
            {
                int bestCount = 0;
                List<ResolvedEvent>? bestHistory = null;
                if (rollout.ParentSessionId is { } parentId && bySession.TryGetValue(parentId, out var candidates))
                {
                    foreach (var candidate in candidates)
                    {
                        if (candidate.FilePath == rollout.FilePath) continue;
                        var resolvedParent = Resolve(candidate, visiting);
                        // 겹치는 prefix 가 0 이면 대조 근거가 없다 → 후보 제외 (시간 fallback 으로)
                        var replayCount = ComparableUsagePrefixCount(rollout.Events, resolvedParent.History);
                        if (replayCount is not { } count || count <= 0) continue;
                        if (bestHistory == null || count > bestCount)
                        {
                            bestCount = count;
                            bestHistory = resolvedParent.History;
                        }
                    }
                }

                ResolvedRollout resolved;
                if (bestHistory != null)
                    resolved = ResolveOwnedEvents(rollout, bestCount, bestHistory.Take(bestCount));
                else if (rollout.ParentSessionId != null)
                    resolved = ResolveOwnedEvents(rollout, FallbackReplayCount(rollout), null);
                else
                    resolved = ResolveOwnedEvents(rollout, 0, null);
                memo[rollout.FilePath] = resolved;
                return resolved;
            }
            finally
            {
                visiting.Remove(rollout.FilePath);
            }
        }

        var result = new List<UsageEntry>();
        foreach (var path in includedPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!byPath.TryGetValue(path, out var rollout)) continue;
            result.AddRange(Resolve(rollout, new HashSet<string>(StringComparer.Ordinal)).Owned);
        }
        // vector 가 id 에 들어 있으므로 keep-max 가 아니라 가장 이른 기록을 보존
        return Dedup.KeepEarliest(result);
    }

    /// <summary>비교 가능한 full usage state 의 공통 prefix 길이. null = 한쪽에 state 가 없어 비교 불가.</summary>
    private static int? ComparableUsagePrefixCount(IReadOnlyList<CodexEvent> child, List<ResolvedEvent> parent)
    {
        if (child.Count == 0) return 0;
        if (parent.Count == 0) return null;
        int count = 0;
        while (count < child.Count && count < parent.Count)
        {
            var childState = child[count].State;
            var parentState = parent[count].State;
            if (childState == null || parentState == null) return null;
            if (childState != parentState) break;
            count++;
        }
        return count;
    }

    /// <summary>부모 대조가 불가능할 때: subagent 는 0, 그 외 fork 는 첫 이벤트와 1초 미만 간격으로 이어지는 burst 를 버림.</summary>
    private static int FallbackReplayCount(ParsedRollout rollout)
    {
        if (rollout.IsSubagent) return 0; // subagent 는 부모 token_count 를 replay 하지 않음
        var events = rollout.Events;
        if (events.Count <= 1) return events.Count == 0 ? 0 : 1;
        int count = 1;
        while (count < events.Count)
        {
            var gap = (events[count].Entry.Date - events[count - 1].Entry.Date).TotalSeconds;
            if (!(gap < ForkReplayMaximumGapSeconds)) break;
            count++;
        }
        return count;
    }

    private static ResolvedRollout ResolveOwnedEvents(ParsedRollout rollout, int replayCount, IEnumerable<ResolvedEvent>? inheritedHistory)
    {
        var history = inheritedHistory?.ToList() ?? new List<ResolvedEvent>();
        var owned = new List<UsageEntry>();
        int epoch = 0;
        UsageVector? previousCumulative = null;
        string? previousOwner = null;

        foreach (var ev in rollout.Events.Skip(replayCount))
        {
            // fork 파일의 unmatched suffix 는 embedded parent meta 뒤에 있어도 child 소유.
            // non-fork 파일의 실제 세션 전환은 이벤트 시점의 세션 id 를 따른다.
            var owner = rollout.ParentSessionId == null ? (ev.SessionId ?? rollout.SessionId) : rollout.SessionId;
            if (!string.Equals(owner, previousOwner, StringComparison.Ordinal))
            {
                epoch = 0;
                previousCumulative = null;
                previousOwner = owner;
            }
            var priorCumulative = previousCumulative;
            if (ev.State is { } st)
            {
                // 누적값이 줄었으면(세션 리셋) 새 epoch
                if (priorCumulative is { } prior && st.Cumulative.IsLowerThan(prior)) epoch++;
                previousCumulative = st.Cumulative;
            }
            else
            {
                previousCumulative = null;
            }

            UsageEntry entry;
            if (owner != null && ev.State is { } state)
            {
                entry = FillTotalOnly(ev.Entry, state, priorCumulative);
                entry.Id = FormattableString.Invariant($"codex|{owner}|{epoch}|{state.Fingerprint}");
            }
            else
            {
                // 누적 usage 또는 세션 id 가 없는 구형 레코드는 위치 기반 임시 id 유지
                entry = Dedup.Clone(ev.Entry);
            }
            owned.Add(entry);
            history.Add(new ResolvedEvent(entry, ev.State));
        }
        return new ResolvedRollout(history, owned);
    }

    /// <summary>
    /// 세션 중간의 #278 턴: last 구성값은 0 인데 직전 이벤트 이후 cumulative.total 이 늘었다 → last.total 을 input 으로.
    /// 항상 복제본을 돌려준다 (파싱 캐시의 객체를 바꾸지 않기 위해).
    /// </summary>
    private static UsageEntry FillTotalOnly(UsageEntry entry, UsageState state, UsageVector? previousCumulative)
    {
        var copy = Dedup.Clone(entry);
        if (entry.Total == 0 && state.Last.Billable == 0 && state.Last.Total > 0
            && previousCumulative is { } previous && state.Cumulative.Total > previous.Total)
        {
            copy.Input = state.Last.Total;
            copy.Output = 0;
            copy.CacheWrite = 0;
            copy.CacheRead = 0;
            copy.CostUnavailable = true;
        }
        return copy;
    }
}
