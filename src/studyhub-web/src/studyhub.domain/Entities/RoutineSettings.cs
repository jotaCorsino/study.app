namespace studyhub.domain.Entities;

public class RoutineSettings
{
    public int DailyGoalMinutes { get; set; } = 0; // 0 significa não configurado
    public List<DayOfWeek> SelectedDaysOfWeek { get; set; } = new();
    public DateTime LastUpdatedAt { get; set; } = DateTime.MinValue;
}
