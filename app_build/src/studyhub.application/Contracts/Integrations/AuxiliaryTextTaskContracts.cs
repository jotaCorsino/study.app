namespace studyhub.application.Contracts.Integrations;

public class AuxiliaryTextTaskRequest
{
    public Guid CourseId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
}

public class AuxiliaryTextTaskResponse
{
    public string Output { get; set; } = string.Empty;
}
