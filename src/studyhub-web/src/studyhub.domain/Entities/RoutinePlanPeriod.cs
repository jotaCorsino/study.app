namespace studyhub.domain.Entities;

public class RoutinePlanPeriod
{
    private int _dailyGoalStudyUnits = 1;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DailyGoalMode GoalMode { get; set; } = DailyGoalMode.TimeMinutes;
    public int DailyGoalMinutes { get; set; }
    public int DailyGoalStudyUnits
    {
        get => _dailyGoalStudyUnits;
        set => _dailyGoalStudyUnits = value <= 0 ? 1 : value;
    }
    public List<DayOfWeek> SelectedDaysOfWeek { get; set; } = [];
}
