namespace studyhub.domain.Entities;

public class Progress
{
    public Guid CourseId { get; set; }
    public int TotalLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int InProgressLessons { get; set; }
    public double OverallPercentage { get; set; }
    public TimeSpan TotalWatchTime { get; set; }
    public DateTime? LastStudiedAt { get; set; }
    public int CurrentStreak { get; set; }
    public Guid? LastLessonId { get; set; }
    public string? LastLessonTitle { get; set; }
}
