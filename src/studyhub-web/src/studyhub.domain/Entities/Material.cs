namespace studyhub.domain.Entities;

public class Material
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ChannelUrl { get; set; } = string.Empty;
    public string Source { get; set; } = "YouTube";
    public string Type { get; set; } = "Video";
    public string VideoId { get; set; } = string.Empty;
    public string PlaylistId { get; set; } = string.Empty;
    public string MatchedQuery { get; set; } = string.Empty;
    public int SubscriberCount { get; set; }
    public double AuthorityScore { get; set; }
    public double RelevanceScore { get; set; }
}
