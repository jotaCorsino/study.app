namespace studyhub.infrastructure.persistence.models;

public class ExternalCourseImportRecord
{
    public Guid CourseId { get; set; }
    public CourseRecord? Course { get; set; }
    public string SchemaVersion { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ExternalCourseId { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;
    public string OriginUrl { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
}
