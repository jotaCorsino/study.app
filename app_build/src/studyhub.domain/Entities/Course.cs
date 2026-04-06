namespace studyhub.domain.Entities;

public class Course
{
    private CourseSourceMetadata _sourceMetadata = new();

    public Guid Id { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public CourseSourceType SourceType { get; set; } = CourseSourceType.LocalFolder;
    public CourseSourceMetadata SourceMetadata
    {
        get => _sourceMetadata;
        set => _sourceMetadata = value ?? new CourseSourceMetadata();
    }
    public string FolderPath
    {
        get => SourceMetadata.RootPath;
        set => SourceMetadata.RootPath = value ?? string.Empty;
    }
    public TimeSpan TotalDuration { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public List<Module> Modules { get; set; } = [];
}
