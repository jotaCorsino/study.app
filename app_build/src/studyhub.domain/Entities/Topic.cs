namespace studyhub.domain.Entities;

public class Topic
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public int Order { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Lesson> Lessons { get; set; } = [];
}
