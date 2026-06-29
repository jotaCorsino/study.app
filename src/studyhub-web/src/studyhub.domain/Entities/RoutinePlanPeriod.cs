namespace studyhub.domain.Entities;

public class RoutinePlanPeriod
{
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int DailyGoalMinutes { get; set; }
    public List<DayOfWeek> SelectedDaysOfWeek { get; set; } = [];
}
