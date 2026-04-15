namespace studyhub.infrastructure.persistence.models;

public class TopicRecord
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public ModuleRecord? Module { get; set; }
    public int Order { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<LessonRecord> Lessons { get; set; } = [];
}
