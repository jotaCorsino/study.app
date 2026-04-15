namespace studyhub.infrastructure.persistence.models;

public class CourseRoadmapRecord
{
    public Guid CourseId { get; set; }
    public CourseRecord? Course { get; set; }
    public string LevelsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}
