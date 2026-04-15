namespace studyhub.domain.Entities;

public class RoadmapItem
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsCurrent { get; set; }
    public List<string> Skills { get; set; } = [];
    public TimeSpan EstimatedDuration { get; set; }
}
