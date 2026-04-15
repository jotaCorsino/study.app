namespace studyhub.infrastructure.persistence.models;

public class ExternalLessonRuntimeStateRecord
{
    public Guid LessonId { get; set; }
    public Guid CourseId { get; set; }
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
