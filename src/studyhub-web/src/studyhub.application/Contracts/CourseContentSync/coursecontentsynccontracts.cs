namespace studyhub.application.Contracts.CourseContentSync;

public enum CourseContentSyncChangeKind
{
    Unchanged = 0,
    New = 1,
    Missing = 2
}

public enum CourseContentSyncPreviewStatus
{
    NotEvaluated = 0,
    Ready = 1,
    NoChanges = 2,
    CourseNotFound = 3,
    UnsupportedCourseSource = 4,
    SourceUnavailable = 5,
    NoVideosFound = 6,
    InsufficientReferenceData = 7,
    AmbiguousStructure = 8,
    ScanFailed = 9,
    AccessDenied = 10,
    Unexpected = 11
}

public enum CourseContentSyncApplyStatus
{
    NotEvaluated = 0,
    Applied = 1,
    NoChanges = 2,
    Blocked = 3,
    Failed = 4
}

public sealed class CourseContentSyncPreviewResult
{
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public DateTime? ScannedAtUtc { get; set; }
    public CourseContentSyncPreviewStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Diagnostics { get; set; } = [];
    public List<CourseContentSyncModulePlanItem> Modules { get; set; } = [];
    public List<CourseContentSyncTopicPlanItem> Topics { get; set; } = [];
    public List<CourseContentSyncLessonPlanItem> Lessons { get; set; } = [];

    public bool Success => Status is CourseContentSyncPreviewStatus.Ready or CourseContentSyncPreviewStatus.NoChanges;
    public bool HasChanges => Modules.Any(IsChange) || Topics.Any(IsChange) || Lessons.Any(IsChange);
    public bool CanApply => Status == CourseContentSyncPreviewStatus.Ready && HasChanges;

    public int ExistingModuleCount => Modules.Count(item => item.ChangeKind != CourseContentSyncChangeKind.New);
    public int CandidateModuleCount => Modules.Count(item => item.ChangeKind != CourseContentSyncChangeKind.Missing);
    public int UnchangedModuleCount => Modules.Count(item => item.ChangeKind == CourseContentSyncChangeKind.Unchanged);
    public int NewModuleCount => Modules.Count(item => item.ChangeKind == CourseContentSyncChangeKind.New);
    public int MissingModuleCount => Modules.Count(item => item.ChangeKind == CourseContentSyncChangeKind.Missing);

    public int ExistingTopicCount => Topics.Count(item => item.ChangeKind != CourseContentSyncChangeKind.New);
    public int CandidateTopicCount => Topics.Count(item => item.ChangeKind != CourseContentSyncChangeKind.Missing);
    public int UnchangedTopicCount => Topics.Count(item => item.ChangeKind == CourseContentSyncChangeKind.Unchanged);
    public int NewTopicCount => Topics.Count(item => item.ChangeKind == CourseContentSyncChangeKind.New);
    public int MissingTopicCount => Topics.Count(item => item.ChangeKind == CourseContentSyncChangeKind.Missing);

    public int ExistingLessonCount => Lessons.Count(item => item.ChangeKind != CourseContentSyncChangeKind.New);
    public int CandidateLessonCount => Lessons.Count(item => item.ChangeKind != CourseContentSyncChangeKind.Missing);
    public int UnchangedLessonCount => Lessons.Count(item => item.ChangeKind == CourseContentSyncChangeKind.Unchanged);
    public int NewLessonCount => Lessons.Count(item => item.ChangeKind == CourseContentSyncChangeKind.New);
    public int MissingLessonCount => Lessons.Count(item => item.ChangeKind == CourseContentSyncChangeKind.Missing);

    private static bool IsChange(CourseContentSyncModulePlanItem item)
        => item.ChangeKind != CourseContentSyncChangeKind.Unchanged;

    private static bool IsChange(CourseContentSyncTopicPlanItem item)
        => item.ChangeKind != CourseContentSyncChangeKind.Unchanged;

    private static bool IsChange(CourseContentSyncLessonPlanItem item)
        => item.ChangeKind != CourseContentSyncChangeKind.Unchanged;
}

public sealed class CourseContentSyncModulePlanItem
{
    public Guid? ExistingModuleId { get; set; }
    public string SourceRelativePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExistingTitle { get; set; } = string.Empty;
    public string DetectedName { get; set; } = string.Empty;
    public int? ExistingOrder { get; set; }
    public int? DetectedOrder { get; set; }
    public CourseContentSyncChangeKind ChangeKind { get; set; }
}

public sealed class CourseContentSyncTopicPlanItem
{
    public Guid? ExistingTopicId { get; set; }
    public Guid? ExistingModuleId { get; set; }
    public string ModuleSourceRelativePath { get; set; } = string.Empty;
    public string SourceRelativePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExistingTitle { get; set; } = string.Empty;
    public string DetectedName { get; set; } = string.Empty;
    public int? ExistingOrder { get; set; }
    public int? DetectedOrder { get; set; }
    public CourseContentSyncChangeKind ChangeKind { get; set; }
}

public sealed class CourseContentSyncLessonPlanItem
{
    public Guid? ExistingLessonId { get; set; }
    public Guid? ExistingTopicId { get; set; }
    public string ModuleSourceRelativePath { get; set; } = string.Empty;
    public string TopicSourceRelativePath { get; set; } = string.Empty;
    public string RelativeFilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExistingTitle { get; set; } = string.Empty;
    public string DetectedName { get; set; } = string.Empty;
    public string DetectedAbsolutePath { get; set; } = string.Empty;
    public TimeSpan? DetectedDuration { get; set; }
    public long? DetectedFileSizeBytes { get; set; }
    public int? ExistingOrder { get; set; }
    public int? DetectedOrder { get; set; }
    public CourseContentSyncChangeKind ChangeKind { get; set; }
}

public sealed class CourseContentSyncApplyResult
{
    public Guid CourseId { get; set; }
    public CourseContentSyncApplyStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public CourseContentSyncPreviewResult Preview { get; set; } = new();
    public int CreatedModuleCount { get; set; }
    public int CreatedTopicCount { get; set; }
    public int CreatedLessonCount { get; set; }
    public int MarkedMissingModuleCount { get; set; }
    public int MarkedMissingTopicCount { get; set; }
    public int MarkedMissingLessonCount { get; set; }
    public int RestoredAvailableItemCount { get; set; }

    public bool Success => Status is CourseContentSyncApplyStatus.Applied or CourseContentSyncApplyStatus.NoChanges;
    public bool AppliedChanges => Status == CourseContentSyncApplyStatus.Applied;
}
