namespace studyhub.application.Contracts.CourseBuilding;

public enum OnlineCourseOperationalStage
{
    Structure = 0,
    Presentation = 1,
    TextRefinement = 2,
    Roadmap = 3,
    SupplementaryMaterials = 4,
    SourceCuration = 5,
    Validation = 6,
    ExternalPlayback = 7
}

public enum OnlineCourseOperationalStatus
{
    NotStarted = 0,
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Skipped = 5,
    Partial = 6
}

public sealed class OnlineCourseStageState
{
    public OnlineCourseOperationalStage Stage { get; set; }
    public OnlineCourseOperationalStatus Status { get; set; } = OnlineCourseOperationalStatus.NotStarted;
    public string Provider { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string LastErrorMessage { get; set; } = string.Empty;
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public DateTime? LastFailedAt { get; set; }
}

public sealed class OnlineCourseRuntimeState
{
    public Guid CourseId { get; set; }
    public Guid? CurrentLessonId { get; set; }
    public double OverallProgressPercentage { get; set; }
    public IReadOnlyList<OnlineCourseStageState> Stages { get; set; } = [];
}

public sealed class OnlineCourseStageExecutionResult
{
    public bool Success { get; set; }
    public Guid CourseId { get; set; }
    public OnlineCourseOperationalStage Stage { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class OnlineCourseStepKeys
{
    public const string OnlinePlanning = "online-planning";
    public const string YouTubeDiscovery = "youtube-discovery";
    public const string SourceCuration = "source-curation";
    public const string CoursePresentation = "course-presentation";
    public const string TextRefinement = "text-refinement";
    public const string OnlineCourseAssembly = "online-course-assembly";
    public const string CoursePersisted = "course-persisted";
    public const string RoadmapGeneration = "roadmap-generation";
    public const string SupplementaryQueryGeneration = "supplementary-query-generation";
    public const string SupplementaryYouTubeDiscovery = "supplementary-youtube-discovery";
    public const string SupplementaryMaterialsGeneration = "supplementary-materials-generation";
    public const string OnlineCourseValidation = "online-course-validation";
    public const string ExternalPlayback = "external-playback";
}

public sealed class ExternalLessonRuntimeState
{
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ExternalUrl { get; set; } = string.Empty;
    public string LastErrorCode { get; set; } = string.Empty;
    public string LastErrorMessage { get; set; } = string.Empty;
    public bool FallbackLaunched { get; set; }
    public DateTime? LastOpenedAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
