namespace CozyOrbit.Core.Game;

/// <summary>오늘의 목표 세이브 칸. 규칙(뽑기·진행·보상)은 DailyGoals.cs</summary>
public sealed class DailyGoalsState
{
    /// <summary>목표를 뽑은 로컬 날짜 "yyyy-MM-dd" (날이 바뀌면 새로 뽑는다)</summary>
    public string? Day { get; set; }
    public List<DailyGoal> Goals { get; set; } = new();
    /// <summary>세 개를 다 받았을 때의 덤을 받았는지</summary>
    public bool BonusClaimed { get; set; }
}

/// <summary>목표 하나: 오늘 시작할 때의 누계(Baseline)에서 Target 만큼 늘면 완료</summary>
public sealed class DailyGoal
{
    public string Id { get; set; } = "";
    public double Target { get; set; }
    public double Baseline { get; set; }
    public bool Claimed { get; set; }
}
