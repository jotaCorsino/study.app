using System.Text.Json.Serialization;

namespace studyhub.domain.Entities;

public enum DailyStudyStatus
{
    Unplanned,
    NotStarted,
    Partial,
    AlmostCompleted,
    Completed
}

public class DailyStudyRecord : IJsonOnDeserialized
{
    private List<Guid> _completedStudyUnitIds = [];

    public Guid CourseId { get; set; }
    public DateTime Date { get; set; }
    public int MinutesStudied { get; set; }
    public int NonLessonMinutesStudied { get; set; }
    public List<LessonStudyCredit> LessonCredits { get; set; } = [];
    public List<Guid> CompletedStudyUnitIds
    {
        get => _completedStudyUnitIds;
        set => _completedStudyUnitIds = (value ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        CompletedStudyUnitIds = CompletedStudyUnitIds;
    }

    [JsonIgnore]
    public int CompletedStudyUnitCount => CompletedStudyUnitIds.Count;
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
