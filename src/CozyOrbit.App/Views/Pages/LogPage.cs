using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views.Pages;

/// <summary>별빛 기록: 오늘 받은 별빛, 소스별 오늘 사용량, 최근 날짜별 지급, 환산 규칙.</summary>
public sealed class LogPage : Page
{
    private readonly StackPanel _root = new() { Spacing = 6 };

    public LogPage(GameHost host) : base(host)
    {
        Content = _root;
    }

    public override void Refresh()
    {
        var s = Host.State;
        var now = DateTimeOffset.Now;
        _root.Children.Clear();

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
        if (Host.Today.Count == 0)
        {
            _root.Children.Add(Text(L.T("아직 오늘 사용량이 없어요. Claude Code · Codex · Gemini · OpenCode · omp 등 이 컴퓨터의 AI 도구 기록을 읽어요 (읽기만 해요).",
                                        "No usage yet today. Reads local logs of AI tools such as Claude Code, Codex, Gemini, OpenCode and omp (read-only)."), "dim"));
        }
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
}
