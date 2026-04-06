namespace studyhub.application.Contracts.Integrations;

public class YoutubeMaterialDiscoveryRequest
{
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseDescription { get; set; } = string.Empty;
    public IReadOnlyList<string> ModuleTitles { get; set; } = [];
    public IReadOnlyList<string> LessonTitles { get; set; } = [];
    public IReadOnlyList<string> SeedQueries { get; set; } = [];
    public IReadOnlyList<string> SelectedSources { get; set; } = [];
}

public class YoutubeMaterialDiscoveryResponse
{
    public IReadOnlyList<YoutubeMaterialCandidate> Candidates { get; set; } = [];
}

public class YoutubeMaterialCandidate
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public bool IsPlaylist { get; set; }
    public double AuthorityScore { get; set; }
    public double RelevanceScore { get; set; }
}
