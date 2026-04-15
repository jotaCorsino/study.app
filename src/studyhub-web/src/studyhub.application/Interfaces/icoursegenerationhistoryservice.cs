using studyhub.application.Contracts.CourseBuilding;

namespace studyhub.application.Interfaces;

public interface ICourseGenerationHistoryService
{
    Task RecordStepAsync(CourseGenerationStepEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CourseGenerationStepEntry>> GetStepStatesAsync(Guid courseId, CancellationToken cancellationToken = default);
}
