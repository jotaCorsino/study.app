using studyhub.shared.Enums;
using studyhub.domain.Entities;

namespace studyhub.infrastructure.persistence.models;

public class LessonRecord
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public TopicRecord? Topic { get; set; }
    public int Order { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public LessonSourceType SourceType { get; set; } = LessonSourceType.LocalFile;
    public string LocalFilePath { get; set; } = string.Empty;
    public string ExternalUrl { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public LessonStatus Status { get; set; }
    public double WatchedPercentage { get; set; }
    public int LastPlaybackPositionSeconds { get; set; }
}
