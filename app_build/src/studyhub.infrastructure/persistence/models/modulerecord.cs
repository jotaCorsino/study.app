namespace studyhub.infrastructure.persistence.models;

public class ModuleRecord
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public CourseRecord? Course { get; set; }
    public int Order { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<TopicRecord> Topics { get; set; } = [];
}
