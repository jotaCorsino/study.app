namespace studyhub.infrastructure.persistence.models;

public class CourseGenerationStepRecord
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string StepKey { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequestJson { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public string LastErrorMessage { get; set; } = string.Empty;
}
