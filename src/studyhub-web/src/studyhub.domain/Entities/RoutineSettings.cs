namespace studyhub.domain.Entities;

public class RoutineSettings
{
    private int _dailyGoalStudyUnits = 1;

    public DailyGoalMode GoalMode { get; set; } = DailyGoalMode.TimeMinutes;
    public int DailyGoalMinutes { get; set; } = 0; // 0 means not configured
    public int DailyGoalStudyUnits
    {
        get => _dailyGoalStudyUnits;
        set => _dailyGoalStudyUnits = value <= 0 ? 1 : value;
    }
    public List<DayOfWeek> SelectedDaysOfWeek { get; set; } = new();
    public DateTime LastUpdatedAt { get; set; } = DateTime.MinValue;
    public List<RoutinePlanPeriod> PlanPeriods { get; set; } = [];
    public List<RoutineSuspensionPeriod> SuspensionPeriods { get; set; } = [];
}
