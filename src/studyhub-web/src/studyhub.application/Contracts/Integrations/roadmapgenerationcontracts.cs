using studyhub.domain.Entities;

namespace studyhub.application.Contracts.Integrations;

public sealed class CourseRoadmapGenerationRequest
{
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = new();
}

public sealed class CourseRoadmapGenerationResponse
{
    public IReadOnlyList<RoadmapLevel> Levels { get; set; } = [];
}
