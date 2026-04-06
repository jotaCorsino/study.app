using studyhub.domain.Entities;

namespace studyhub.application.Contracts.Integrations;

public sealed class SupplementaryMaterialsGenerationRequest
{
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseDescription { get; set; } = string.Empty;
    public IReadOnlyList<string> ModuleTitles { get; set; } = [];
    public IReadOnlyList<string> SeedQueries { get; set; } = [];
}

public sealed class SupplementaryMaterialsGenerationResponse
{
    public IReadOnlyList<Material> Materials { get; set; } = [];
    public IReadOnlyList<string> Queries { get; set; } = [];
    public IReadOnlyList<string> Notes { get; set; } = [];
}
