namespace studyhub.domain.Entities;

public enum RoutineSuspensionReason
{
    Paused = 0,
    Completed = 1
}

public class RoutineSuspensionPeriod
{
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public RoutineSuspensionReason Reason { get; set; } = RoutineSuspensionReason.Paused;
}
