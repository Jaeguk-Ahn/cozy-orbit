using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CozyOrbit.Core.Art;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;
using CozyOrbit.Core.Usage;

namespace CozyOrbit.App.Views.Pages;

/// <summary>별빛 기록: 날짜별 사용량 그래프, 오늘 받은 별빛, 소스별 오늘 사용량, 최근 날짜별 지급, 환산 규칙.</summary>
public sealed class LogPage : Page
{
    private enum Metric { Tokens, Weighted, Starlight, Candy }

    private readonly StackPanel _root = new() { Spacing = 6 };
    /// <summary>그래프 기간 (7 또는 30일)</summary>
    private int _rangeDays = 7;
    /// <summary>처음엔 가중 토큰 — 별빛 장부로 채운 옛 날짜엔 토큰 원값이 없어 "토큰" 그래프가 비어 보인다. 버튼으로 고르면 그대로 둔다.</summary>
    private Metric _metric = Metric.Weighted;

    public LogPage(GameHost host) : base(host)
    {
        Content = _root;
    }

    // AI 도구 찾기 결과가 스캔보다 늦게 오면 빈 화면 안내를 다시 그린다 (화면에 붙어 있는 동안만 구독)
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Host.ProvidersDetected += Refresh;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Host.ProvidersDetected -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Refresh()
    {
        var s = Host.State;
        var now = DateTimeOffset.Now;
        _root.Children.Clear();

        _root.Children.Add(ChartCard(s, now));

        double todayW = s.Ledger.CreditedWeighted.GetValueOrDefault(StarlightLedger.DayKey(now));
        double todayLight = Balance.DailyStarlight(todayW);
        double nextTen = Balance.WeightedForDailyStarlight(todayLight + 600) - todayW;

        var big = new TextBlock { Text = Fmt.Duration(todayLight), Classes = { "big" }, Foreground = Brush("StarlightBrush") };
        _root.Children.Add(Card(Column(4,
            Row(6, new PixelIcon { Sprite = "icon_starlight", Scale = 2, VerticalAlignment = VerticalAlignment.Center }, Text(L.T("오늘 받은 별빛", "Starlight earned today"), "h")),
            big,
            Text(L.T($"오늘 가중 토큰 {Fmt.Compact(todayW)} · 별빛 10분 더 받으려면 약 {Fmt.Compact(nextTen)}",
                     $"Weighted tokens today {Fmt.Compact(todayW)} · ~{Fmt.Compact(nextTen)} more for +10 min"), "dim"),
            Text(L.T("많이 쓸수록 조금씩 덜 쌓여요 (날마다 새로 시작). 배터리가 가득 차면 넘친 별빛은 별가루가 돼요.",
                     "The more you use, the slower it fills (resets daily). When the battery is full, extra light turns into stardust."), "dim"))));

        _root.Children.Add(Header(L.T("오늘 읽은 AI 사용량", "AI usage read today"), "icon_log"));
        if (Host.Today.Count == 0) _root.Children.Add(NoUsageToday(s));
        foreach (var p in Host.Today)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var name = Text(p.DisplayName);
            var value = Text(L.T($"{Fmt.Compact(p.Tokens)} 토큰 · 가중 {Fmt.Compact(p.Weighted)}", $"{Fmt.Compact(p.Tokens)} tokens · weighted {Fmt.Compact(p.Weighted)}"), "dim");
            Grid.SetColumn(value, 1);
            grid.Children.Add(name);
            grid.Children.Add(value);
            var col = Column(2, grid);
            if (p.Error != null) col.Children.Add(Text(L.T($"읽기 실패 (다음에 다시): {p.Error}", $"Read failed (will retry): {p.Error}"), fg: Brush("WarnBrush")));
            _root.Children.Add(Card(col));
        }

        string scanned = Host.Scanning ? L.T("읽는 중…", "Reading…")
            : Host.LastScanFinished is { } t ? L.T($"마지막으로 읽은 시각 {t:HH:mm:ss} (2분마다)", $"Last read {t:HH:mm:ss} (every 2 min)")
            : L.T("아직 읽지 않았어요", "Not read yet");
        var rescan = new Button { Content = L.T("지금 다시 읽기", "Read now"), IsEnabled = !Host.Scanning };
        rescan.Click += (_, _) => _ = Host.ScanNowAsync();
        var scanRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var scanText = Text(scanned, "dim");
        scanText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(rescan, 1);
        scanRow.Children.Add(scanText);
        scanRow.Children.Add(rescan);
        _root.Children.Add(scanRow);

        _root.Children.Add(Header(L.T("최근 날짜별 별빛", "Starlight by day"), "icon_starlight"));
        foreach (var (day, w) in s.Ledger.CreditedWeighted.OrderByDescending(kv => kv.Key, StringComparer.Ordinal).Take(7))
        {
            var label = DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString(L.IsKorean ? "M월 d일 (ddd)" : "MMM d (ddd)", L.Culture) : day;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var value = Text(L.T($"가중 {Fmt.Compact(w)} → {Fmt.Duration(Balance.DailyStarlight(w))}", $"weighted {Fmt.Compact(w)} → {Fmt.Duration(Balance.DailyStarlight(w))}"), "dim");
            Grid.SetColumn(value, 1);
            grid.Children.Add(Text(label));
            grid.Children.Add(value);
            _root.Children.Add(grid);
        }

        _root.Children.Add(Header(L.T("환산 규칙", "How it converts"), "icon_gear"));
        _root.Children.Add(Text(L.T(
            $"가중 토큰 = 출력 ×{Balance.WeightOutput} + 입력 ×{Balance.WeightInput} + 캐시 쓰기 ×{Balance.WeightCacheWrite} + 캐시 읽기 ×{Balance.WeightCacheRead}\n" +
            $"하루 별빛(초) = {Balance.DailyStarlightScale} × ln(1 + 가중 토큰 ÷ {Fmt.Compact(Balance.DailyStarlightPivot)})\n별빛 1 = 부스트 1초",
            $"Weighted = output ×{Balance.WeightOutput} + input ×{Balance.WeightInput} + cache write ×{Balance.WeightCacheWrite} + cache read ×{Balance.WeightCacheRead}\n" +
            $"Daily starlight (s) = {Balance.DailyStarlightScale} × ln(1 + weighted ÷ {Fmt.Compact(Balance.DailyStarlightPivot)})\n1 starlight = 1 second of boost"), "dim"));
    }

    /// <summary>
    /// 오늘 사용량이 없을 때. 켜 둔 도구의 기록 폴더를 하나도 못 찾았으면 친절한 빈 화면(기본 속도·태양 돛·읽는 도구 목록),
    /// 찾았는데 오늘 쓴 게 없으면 찾은 도구 이름과 함께 짧게. 아직 찾아보기 전이면 예전처럼 한 줄.
    /// </summary>
    private Control NoUsageToday(GameState s)
    {
        var disabled = s.Settings.DisabledProviders;
        var enabledFound = Host.Providers
            .Where(p => !disabled.Contains(p.Id) && Host.ProviderFound.GetValueOrDefault(p.Id))
            .Select(p => p.DisplayName).ToList();
        string supported = string.Join(" · ", Host.Providers.Select(p => p.DisplayName));

        switch (ProviderDetection.AnyEnabledFound(Host.ProviderFound, Host.Providers.Select(p => p.Id), disabled))
        {
            case false:
                var col = Column(4,
                    Row(6, new PixelIcon { Sprite = Rendering.SkinArt.CaptainFur(s), Scale = 2, Animate = true, VerticalAlignment = VerticalAlignment.Center },
                        Text(L.T("이 컴퓨터에서 AI 도구 기록을 찾지 못했어요", "No AI tool logs found on this computer"), "h")),
                    Text(L.T("괜찮아요, 도토리호는 기본 속도로 계속 날아가요. 연구 탭의 태양 돛을 올리면 토큰 없이도 별빛이 조금씩 모여요.",
                             "That's okay — the ship keeps flying at base speed. The Solar Sail research gathers a little starlight even without tokens.")),
                    Text(L.T($"읽을 수 있는 도구: {supported}", $"Supported tools: {supported}"), "dim"));
                if (disabled.Count > 0)
                    col.Children.Add(Text(L.T("설정에서 꺼 둔 도구는 빼고 찾았어요.", "Tools turned off in Settings aren't counted."), "dim"));
                return Card(col);
            case true:
                string names = string.Join(" · ", enabledFound);
                return Text(L.T($"아직 오늘 사용량이 없어요. 찾은 도구: {names} — 쓰고 나면 다음에 읽을 때 별빛이 돼요 (읽기만 해요).",
                                $"No usage yet today. Found: {names} — use one and it turns into starlight on the next read (read-only)."), "dim");
            default:
                return Text(L.T($"아직 오늘 사용량이 없어요. 이 컴퓨터의 AI 도구 기록을 읽어요 (읽기만 해요): {supported}",
                                $"No usage yet today. Reads local logs of these AI tools (read-only): {supported}"), "dim");
        }
    }

    // ── 날짜별 사용량 그래프 ──

    private Control ChartCard(GameState s, DateTimeOffset now)
    {
        var today = now.ToLocalTime().Date;
        var points = new ChartPoint[_rangeDays];
        double sum = 0;
        for (int i = 0; i < _rangeDays; i++)
        {
            var day = today.AddDays(i - (_rangeDays - 1));
            string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            double v = Value(s, key);
            sum += v;
            points[i] = new ChartPoint(day.ToString("M/d", CultureInfo.InvariantCulture), v,
                day.ToString(L.IsKorean ? "M월 d일 (ddd)" : "MMM d (ddd)", L.Culture));
        }

        var (bar, lit) = _metric switch
        {
            Metric.Tokens => (Palette.Blue, Palette.Sky),
            Metric.Weighted => (Palette.Purple, Palette.Lilac),
            Metric.Starlight => (Palette.Gold, Palette.Butter),
            _ => (Palette.Rose, Palette.Pink),
        };
        var chart = new UsageChart
        {
            Points = points,
            Format = Format(_metric),
            BarColor = Color.FromUInt32(bar),
            HighlightColor = Color.FromUInt32(lit),
            Margin = new Thickness(0, 4, 0, 0),
        };

        var title = Row(6, new PixelIcon { Sprite = "icon_log", Scale = 1, VerticalAlignment = VerticalAlignment.Center }, Text(L.T("날짜별 사용량", "Usage by day"), "h"));
        title.VerticalAlignment = VerticalAlignment.Center;
        var range = Row(4,
            Toggle(L.T("7일", "7d"), _rangeDays == 7, () => _rangeDays = 7),
            Toggle(L.T("30일", "30d"), _rangeDays == 30, () => _rangeDays = 30));
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(range, 1);
        head.Children.Add(title);
        head.Children.Add(range);

        var metrics = Row(4,
            Toggle(L.T("토큰", "Tokens"), _metric == Metric.Tokens, () => _metric = Metric.Tokens),
            Toggle(L.T("가중 토큰", "Weighted"), _metric == Metric.Weighted, () => _metric = Metric.Weighted),
            Toggle(L.T("별빛", "Starlight"), _metric == Metric.Starlight, () => _metric = Metric.Starlight),
            Toggle(L.T("별사탕", "Candy"), _metric == Metric.Candy, () => _metric = Metric.Candy));

        var fmt = Format(_metric);
        string totals = sum > 0
            ? L.T($"최근 {_rangeDays}일 합계 {fmt(sum)} · 하루 평균 {fmt(sum / _rangeDays)}",
                  $"Last {_rangeDays} days: total {fmt(sum)} · daily average {fmt(sum / _rangeDays)}")
            : L.T($"최근 {_rangeDays}일 기록이 아직 없어요. AI 도구를 쓰면 날마다 쌓여요.",
                  $"Nothing recorded in the last {_rangeDays} days yet. It fills in day by day as you use AI tools.");

        return Card(Column(4, head, metrics, chart, Text(totals, "dim")));
    }

    /// <summary>그날 값. 가중 토큰·별빛은 장부가 기록보다 앞서 있으면(다음 기록 전) 장부 값을 쓴다.</summary>
    private double Value(GameState s, string day)
    {
        var d = s.History.GetValueOrDefault(day);
        double credited = s.Ledger.CreditedWeighted.GetValueOrDefault(day);
        return _metric switch
        {
            Metric.Tokens => d?.Tokens ?? 0,
            Metric.Weighted => Math.Max(d?.Weighted ?? 0, credited),
            Metric.Starlight => Math.Max(d?.Starlight ?? 0, Balance.DailyStarlight(credited)),
            _ => d?.Candy ?? 0,
        };
    }

    private static Func<double, string> Format(Metric m) => m switch
    {
        Metric.Starlight => Fmt.Duration,
        Metric.Candy => v => L.T($"{CandyText(v)}개", CandyText(v)),
        _ => Fmt.Compact,
    };

    private static string CandyText(double v) => v < 10 ? Fmt.Number(v, 1) : Fmt.Compact(v);

    /// <summary>작은 선택 버튼. 고른 것은 별빛색으로 칠하고 눌리지 않게 (Fluent 의 accent 파랑을 피해 직접 칠한다)</summary>
    private Button Toggle(string text, bool on, Action select)
    {
        var b = new Button { Content = text, Padding = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            b.Background = Brush("StarlightBrush");
            b.Foreground = Brush("OutlineBrush");
            b.IsHitTestVisible = false;
        }
        b.Click += (_, _) =>
        {
            select();
            Refresh();
        };
        return b;
    }
}
