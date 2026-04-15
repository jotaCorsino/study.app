namespace studyhub.application.Contracts.ExternalImport;

public sealed class ExternalCourseImportResult
{
    public ExternalCourseImportStatus Status { get; set; }
    public ExternalCourseImportErrorKind ErrorKind { get; set; } = ExternalCourseImportErrorKind.None;
    public Guid? CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public int DisciplineCount { get; set; }
    public int ModuleCount { get; set; }
    public int LessonCount { get; set; }
    public int SkippedLessonCount { get; set; }
    public int AssessmentCount { get; set; }
    public string Message { get; set; } = string.Empty;

    public bool Success => Status is ExternalCourseImportStatus.Imported or ExternalCourseImportStatus.Updated;

    public static ExternalCourseImportResult Failed(string message, ExternalCourseImportErrorKind errorKind)
        => new()
        {
            Status = ExternalCourseImportStatus.Failed,
            ErrorKind = errorKind,
            Message = message
        };
}

public enum ExternalCourseImportStatus
{
    Imported = 0,
    Updated = 1,
    Failed = 2
}

public enum ExternalCourseImportErrorKind
{
    None = 0,
    InvalidPayload = 1,
    UnsupportedSchemaVersion = 2,
    MissingRequiredData = 3,
    NoSupportedLessons = 4,
    PersistenceFailed = 5,
    Unexpected = 6
}
