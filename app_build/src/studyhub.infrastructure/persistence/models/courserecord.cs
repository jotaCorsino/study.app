using studyhub.domain.Entities;

namespace studyhub.infrastructure.persistence.models;

public class CourseRecord
{
    public Guid Id { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public CourseSourceType SourceType { get; set; } = CourseSourceType.LocalFolder;
    public string SourceMetadataJson { get; set; } = string.Empty;
    public int TotalDurationMinutes { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public Guid? CurrentLessonId { get; set; }
    public List<ModuleRecord> Modules { get; set; } = [];
}
