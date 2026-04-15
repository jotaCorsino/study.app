using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface IRoadmapService
{
    Task<List<RoadmapLevel>> GetRoadmapByCourseAsync(Guid courseId);
    Task GenerateRoadmapAsync(Guid courseId);
    Task SaveRoadmapAsync(Guid courseId, List<RoadmapLevel> roadmapLevels);
}
