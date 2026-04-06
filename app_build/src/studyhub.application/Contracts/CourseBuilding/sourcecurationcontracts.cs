namespace studyhub.application.Contracts.CourseBuilding;

public sealed class SourceCurationRequest
{
    public OnlineCourseIntentRequest Intent { get; set; } = new();
    public OnlineCoursePlanningResponse Planning { get; set; } = new();
    public YouTubeCourseDiscoveryResponse Discovery { get; set; } = new();
}

public sealed class SourceCurationResponse
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> SelectedQueries { get; set; } = [];
    public IReadOnlyList<YouTubeSourceCandidate> SelectedSources { get; set; } = [];
    public IReadOnlyList<CuratedModuleSelection> ModuleSelections { get; set; } = [];
}

public sealed class CuratedModuleSelection
{
    public int Order { get; set; }
    public string ModuleTitle { get; set; } = string.Empty;
    public string ModuleObjective { get; set; } = string.Empty;
    public IReadOnlyList<CuratedSourceAssignment> Sources { get; set; } = [];
}

public sealed class CuratedSourceAssignment
{
    public string SourceKind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string PlaylistId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public double AuthorityScore { get; set; }
    public double RelevanceScore { get; set; }
    public IReadOnlyList<YouTubeVideoDescriptor> Lessons { get; set; } = [];
}
