namespace studyhub.application.Contracts.CourseBuilding;

public sealed class YouTubeCourseDiscoveryRequest
{
    public Guid CourseId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string RegionCode { get; set; } = "US";
    public IReadOnlyList<string> Queries { get; set; } = [];
    public int MaxResultsPerQuery { get; set; } = 8;
}

public sealed class YouTubeCourseDiscoveryResponse
{
    public IReadOnlyList<YouTubeSourceCandidate> Candidates { get; set; } = [];
    public IReadOnlyList<YouTubePlaylistBundle> PlaylistBundles { get; set; } = [];
}

public sealed class YouTubeSourceCandidate
{
    public string SourceKind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string PlaylistId { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChannelUrl { get; set; } = string.Empty;
    public long SubscriberCount { get; set; }
    public int ItemCount { get; set; }
    public TimeSpan Duration { get; set; }
    public double AuthorityScore { get; set; }
    public double RelevanceScore { get; set; }
    public string MatchedQuery { get; set; } = string.Empty;
}

public sealed class YouTubePlaylistBundle
{
    public string PlaylistId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public long SubscriberCount { get; set; }
    public IReadOnlyList<YouTubeVideoDescriptor> Videos { get; set; } = [];
}

public sealed class YouTubeVideoDescriptor
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public int OrderHint { get; set; }
    public double RelevanceScore { get; set; }
}
