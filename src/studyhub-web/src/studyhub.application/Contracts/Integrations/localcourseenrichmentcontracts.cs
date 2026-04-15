namespace studyhub.application.Contracts.Integrations;

public sealed class LocalCourseEnrichmentRequest
{
    public Guid CourseId { get; set; }
    public bool StructureChanged { get; set; } = true;
    public bool ForceRefresh { get; set; }
    public bool RefreshPresentation { get; set; } = true;
    public bool RefreshTextRefinement { get; set; } = true;
    public bool RefreshRoadmap { get; set; } = true;
    public bool RefreshMaterials { get; set; } = true;
}

public sealed class LocalCourseEnrichmentResponse
{
    public Guid CourseId { get; set; }
    public bool PresentationUpdated { get; set; }
    public bool TextRefinementUpdated { get; set; }
    public bool RoadmapUpdated { get; set; }
    public bool MaterialsUpdated { get; set; }
    public IReadOnlyList<string> CompletedSteps { get; set; } = [];
}
