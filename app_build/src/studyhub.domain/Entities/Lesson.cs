using studyhub.shared.Enums;

namespace studyhub.domain.Entities;

public class Lesson
{
    private string _localFilePath = string.Empty;

    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public int Order { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public LessonSourceType SourceType { get; set; } = LessonSourceType.LocalFile;
    public string LocalFilePath
    {
        get => _localFilePath;
        set => _localFilePath = value ?? string.Empty;
    }
    public string ExternalUrl { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string FilePath
    {
        get => LocalFilePath;
        set => LocalFilePath = value;
    }
    public TimeSpan Duration { get; set; }
    public LessonStatus Status { get; set; } = LessonStatus.NotStarted;
    public double WatchedPercentage { get; set; }
    public TimeSpan LastPlaybackPosition { get; set; }
}
