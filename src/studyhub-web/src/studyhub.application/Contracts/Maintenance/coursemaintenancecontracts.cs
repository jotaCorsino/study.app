namespace studyhub.application.Contracts.Maintenance;

public sealed class CourseMaintenanceOperationResult
{
    public bool Success { get; set; }
    public Guid CourseId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int AffectedItems { get; set; }
}
