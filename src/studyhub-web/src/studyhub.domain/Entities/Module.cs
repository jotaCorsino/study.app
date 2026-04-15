namespace studyhub.domain.Entities;

public class Module
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public int Order { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Topic> Topics { get; set; } = [];
}
