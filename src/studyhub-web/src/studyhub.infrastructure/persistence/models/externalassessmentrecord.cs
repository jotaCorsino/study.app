namespace studyhub.infrastructure.persistence.models;

public class ExternalAssessmentRecord
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public CourseRecord? Course { get; set; }
    public string DisciplineExternalId { get; set; } = string.Empty;
    public string AssessmentExternalId { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? WeightPercentage { get; set; }
    public DateTime? AvailabilityStartAt { get; set; }
    public DateTime? AvailabilityEndAt { get; set; }
    public double? Grade { get; set; }
    public string MetadataJson { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
