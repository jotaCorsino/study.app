namespace studyhub.infrastructure.persistence.models;

public class CourseImportSnapshotRecord
{
    public Guid CourseId { get; set; }
    public CourseRecord? Course { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string RootFolderPath { get; set; } = string.Empty;
    public string StructureJson { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
}
