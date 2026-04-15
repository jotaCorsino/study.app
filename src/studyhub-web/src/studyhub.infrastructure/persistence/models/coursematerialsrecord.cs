namespace studyhub.infrastructure.persistence.models;

public class CourseMaterialsRecord
{
    public Guid CourseId { get; set; }
    public CourseRecord? Course { get; set; }
    public string MaterialsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}
