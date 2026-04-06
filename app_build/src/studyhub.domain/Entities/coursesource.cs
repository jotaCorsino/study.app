namespace studyhub.domain.Entities;

public enum CourseSourceType
{
    LocalFolder = 0,
    OnlineCurated = 1
}

public enum LessonSourceType
{
    LocalFile = 0,
    ExternalVideo = 1
}

public class CourseSourceMetadata
{
    public string RootPath { get; set; } = string.Empty;
    public DateTime? ImportedAt { get; set; }
    public DateTime? LastEnrichedAt { get; set; }
    public string ScanVersion { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string RequestedTopic { get; set; } = string.Empty;
    public string RequestedObjective { get; set; } = string.Empty;
    public List<string> SearchQueries { get; set; } = [];
    public List<string> SourceUrls { get; set; } = [];
    public List<string> PlaylistIds { get; set; } = [];
    public List<string> VideoIds { get; set; } = [];
    public List<string> CompletedSteps { get; set; } = [];
    public string GenerationSummary { get; set; } = string.Empty;
    public List<CuratedCourseSourceReference> CuratedSources { get; set; } = [];
}

public class CuratedCourseSourceReference
{
    public string Provider { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChannelUrl { get; set; } = string.Empty;
    public string PlaylistId { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public long? ChannelAudienceSize { get; set; }
    public double AuthorityScore { get; set; }
    public double RelevanceScore { get; set; }
}
