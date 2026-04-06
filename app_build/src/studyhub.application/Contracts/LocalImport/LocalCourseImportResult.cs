namespace studyhub.application.Contracts.LocalImport;

public class LocalCourseImportResult
{
    public LocalCourseImportStatus Status { get; set; }
    public LocalCourseImportErrorKind ErrorKind { get; set; } = LocalCourseImportErrorKind.None;
    public Guid? CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public string? FolderPath { get; set; }
    public int ModuleCount { get; set; }
    public int TopicCount { get; set; }
    public int LessonCount { get; set; }
    public string? Message { get; set; }

    public bool Success => Status is LocalCourseImportStatus.Imported or LocalCourseImportStatus.Updated;

    public static LocalCourseImportResult Cancelled()
        => new() { Status = LocalCourseImportStatus.Cancelled };

    public static LocalCourseImportResult Failed(string message, LocalCourseImportErrorKind errorKind = LocalCourseImportErrorKind.Unexpected)
        => new() { Status = LocalCourseImportStatus.Failed, ErrorKind = errorKind, Message = message };
}

public enum LocalCourseImportStatus
{
    Cancelled = 0,
    Imported = 1,
    Updated = 2,
    Failed = 3
}

public enum LocalCourseImportErrorKind
{
    None = 0,
    InvalidFolder = 1,
    NoVideosFound = 2,
    ScanFailed = 3,
    PersistenceFailed = 4,
    Unexpected = 5
}
