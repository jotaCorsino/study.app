namespace studyhub.domain.Entities;

public enum DailyStudyStatus
{
    Unplanned,
    NotStarted,
    Partial,
    AlmostCompleted,
    Completed
}

public class DailyStudyRecord
{
    public Guid CourseId { get; set; }
    public DateTime Date { get; set; }
    public int MinutesStudied { get; set; }
    public int NonLessonMinutesStudied { get; set; }
    public List<LessonStudyCredit> LessonCredits { get; set; } = [];
    public int DailyGoalMinutesAtTheTime { get; set; }
    public double CompliancePercentage => DailyGoalMinutesAtTheTime > 0 
        ? Math.Min(100.0, (double)MinutesStudied / DailyGoalMinutesAtTheTime * 100) 
        : 0;

    public DailyStudyStatus Status { get; set; } = DailyStudyStatus.NotStarted;
}

public class LessonStudyCredit
{
    public Guid LessonId { get; set; }
    public int MinutesCredited { get; set; }
}
