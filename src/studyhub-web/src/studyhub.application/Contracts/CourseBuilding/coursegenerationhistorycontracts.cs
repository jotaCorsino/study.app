namespace studyhub.application.Contracts.CourseBuilding;

public enum CourseGenerationStepStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}

public sealed class CourseGenerationStepEntry
{
    public Guid CourseId { get; set; }
    public string StepKey { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public CourseGenerationStepStatus Status { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSucceededAt { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public string LastErrorMessage { get; set; } = string.Empty;
}
