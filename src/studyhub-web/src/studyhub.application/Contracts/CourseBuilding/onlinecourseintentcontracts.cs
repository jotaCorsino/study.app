namespace studyhub.application.Contracts.CourseBuilding;

public sealed class OnlineCourseIntentRequest
{
    public Guid CourseId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Language { get; set; } = "pt-BR";
    public string PreferredProvider { get; set; } = "YouTube";
}

public sealed class OnlineCourseCreationResult
{
    public bool Success { get; set; }
    public Guid? CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
