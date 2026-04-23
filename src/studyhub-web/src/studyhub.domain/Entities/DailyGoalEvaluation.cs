namespace studyhub.domain.Entities;

public class DailyGoalEvaluation
{
    public Guid CourseId { get; set; }
    public DateTime Date { get; set; }
    public DailyStudyStatus RawStatus { get; set; } = DailyStudyStatus.NotStarted;
    public int MinutesStudied { get; set; }
    public int DailyGoalMinutesAtTheTime { get; set; }
    public int ExtraMinutes { get; set; }
    public int MissingMinutes { get; set; }
    public bool IsMonthlyCreditApplied { get; set; }
    public double RawCompliancePercentage { get; set; }
    public double EffectiveCompliancePercentage { get; set; }
    public bool CountsAsEffectiveGoalMet { get; set; }
    public bool IsPlannedDay { get; set; }
    public bool IsFutureDay { get; set; }
}
