namespace studyhub.application.Contracts.CourseSourceManagement;

public enum CourseSourceLocationCompatibility
{
    NotEvaluated = 0,
    ExactMatch = 1,
    CompatibleWithNewContent = 2,
    PartialMatch = 3,
    Incompatible = 4,
    InsufficientReferenceData = 5
}

public enum CourseSourceLocationErrorKind
{
    None = 0,
    CourseNotFound = 1,
    UnsupportedCourseSource = 2,
    InvalidFolder = 3,
    NoVideosFound = 4,
    AccessDenied = 5,
    ScanFailed = 6,
    PersistenceFailed = 7,
    Unexpected = 8
}

public sealed class CourseSourceLocationValidationResult
{
    public Guid CourseId { get; set; }
    public string CurrentRootPath { get; set; } = string.Empty;
    public string CandidateRootPath { get; set; } = string.Empty;
    public int ExistingLessonCount { get; set; }
    public int ExistingComparableLessonCount { get; set; }
    public int CandidateLessonCount { get; set; }
    public int MatchedExistingLessonCount { get; set; }
    public int MissingExistingLessonCount { get; set; }
    public int NewCandidateLessonCount { get; set; }
    public CourseSourceLocationCompatibility Compatibility { get; set; }
    public CourseSourceLocationErrorKind ErrorKind { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsCurrentLocation { get; set; }

    public bool Success =>
        ErrorKind == CourseSourceLocationErrorKind.None &&
        Compatibility != CourseSourceLocationCompatibility.NotEvaluated;

    public bool CanApplyDirectly =>
        Success &&
        !IsCurrentLocation &&
        Compatibility is CourseSourceLocationCompatibility.ExactMatch or
            CourseSourceLocationCompatibility.CompatibleWithNewContent;

    public bool RequiresExplicitConfirmation =>
        Success && Compatibility == CourseSourceLocationCompatibility.PartialMatch;
}

public sealed class ChangeCourseSourceLocationRequest
{
    public Guid CourseId { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public bool ConfirmPartialMatch { get; set; }
}

public enum CourseSourceLocationChangeStatus
{
    NotEvaluated = 0,
    Changed = 1,
    Unchanged = 2,
    Blocked = 3,
    PartialConfirmationRequired = 4,
    ValidationFailed = 5,
    Failed = 6
}

public sealed class CourseSourceLocationChangeResult
{
    public Guid CourseId { get; set; }
    public CourseSourceLocationChangeStatus Status { get; set; }
    public CourseSourceLocationErrorKind ErrorKind { get; set; }
    public string Message { get; set; } = string.Empty;
    public CourseSourceLocationValidationResult Validation { get; set; } = new();

    public bool Success =>
        Status is CourseSourceLocationChangeStatus.Changed or
            CourseSourceLocationChangeStatus.Unchanged;

    public bool Applied => Status == CourseSourceLocationChangeStatus.Changed;
}
