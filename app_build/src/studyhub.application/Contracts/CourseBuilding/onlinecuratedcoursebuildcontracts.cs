using studyhub.domain.Entities;

namespace studyhub.application.Contracts.CourseBuilding;

public class OnlineCuratedCourseBuildRequest
{
    public string Theme { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string PreferredProvider { get; set; } = "YouTube";
    public List<string> SeedQueries { get; set; } = [];
}

public class OnlineCuratedCourseBlueprint
{
    public CourseSourceType SourceType { get; set; } = CourseSourceType.OnlineCurated;
    public string Theme { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public bool SupportsMultipleSources { get; set; } = true;
    public bool BreakPlaylistsIntoLessons { get; set; } = true;
    public bool OrganizeByLearningProgression { get; set; } = true;
    public List<string> DiscoveryQueries { get; set; } = [];
    public List<OnlineCuratedSourceCandidate> CandidateSources { get; set; } = [];
    public List<OnlineCuratedAssemblyStep> AssemblySteps { get; set; } = [];
    public CourseSourceMetadata SourceMetadataTemplate { get; set; } = new();
}

public class OnlineCuratedSourceCandidate
{
    public string Provider { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChannelUrl { get; set; } = string.Empty;
    public long? ChannelAudienceSize { get; set; }
    public double AuthorityScore { get; set; }
    public double RelevanceScore { get; set; }
    public bool IsPlaylist { get; set; }
    public string PlaylistId { get; set; } = string.Empty;
}

public class OnlineCuratedAssemblyStep
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
