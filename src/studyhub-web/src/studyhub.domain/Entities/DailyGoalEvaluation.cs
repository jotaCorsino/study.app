namespace studyhub.domain.Entities;

public class DailyGoalEvaluation
{
    public Guid CourseId { get; set; }
    public DateTime Date { get; set; }
    public DailyGoalMode GoalMode { get; set; } = DailyGoalMode.TimeMinutes;
    public int GoalValueAtTheTime { get; set; }
    public int CompletedGoalValue { get; set; }
    public string GoalUnit { get; set; } = "minutes";
    public DailyStudyStatus RawStatus { get; set; } = DailyStudyStatus.NotStarted;
    public int MinutesStudied { get; set; }
    public int DailyGoalMinutesAtTheTime { get; set; }
    public int ExtraMinutes { get; set; }
    public int MissingMinutes { get; set; }
    public int ConsumedMonthlyCreditMinutes { get; set; }
    public int AvailableMonthlyCreditMinutes { get; set; }
    public bool IsMonthlyCreditApplied { get; set; }
    public double RawCompliancePercentage { get; set; }
    public double EffectiveCompliancePercentage { get; set; }
    public bool CountsAsEffectiveGoalMet { get; set; }
    public bool IsPlannedDay { get; set; }
    public bool IsFutureDay { get; set; }
}
