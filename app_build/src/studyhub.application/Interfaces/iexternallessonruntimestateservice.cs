using studyhub.application.Contracts.CourseBuilding;

namespace studyhub.application.Interfaces;

public interface IExternalLessonRuntimeStateService
{
    Task RecordOpenedAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, CancellationToken cancellationToken = default);
    Task RecordReadyAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, string errorCode, string errorMessage, bool fallbackLaunched, CancellationToken cancellationToken = default);
    Task<ExternalLessonRuntimeState?> GetStateAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalLessonRuntimeState>> GetCourseStatesAsync(Guid courseId, CancellationToken cancellationToken = default);
}
