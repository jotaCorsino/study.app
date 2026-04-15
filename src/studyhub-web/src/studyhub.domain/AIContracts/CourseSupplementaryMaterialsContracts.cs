namespace studyhub.domain.AIContracts;

public class CourseSupplementaryMaterialsRequestContract
{
    public SupplementaryMatchInformationContract CourseInformation { get; set; } = new();
    public string CurationGoal { get; set; } = string.Empty;
}

public class SupplementaryMatchInformationContract
{
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Modules { get; set; } = new();
    public List<string> LessonHighlights { get; set; } = new();
    public List<string> RoadmapHighlights { get; set; } = new();
    public List<string> SelectedSources { get; set; } = new();
    public List<string> ExistingQueries { get; set; } = new();
    public string RequestedTopic { get; set; } = string.Empty;
    public string RequestedObjective { get; set; } = string.Empty;
}

public class CourseSupplementaryMaterialsResponseContract
{
    public List<string> RecommendedSearchQueries { get; set; } = new();
    public List<string> CurationNotes { get; set; } = new();
}
