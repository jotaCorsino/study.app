using studyhub.application.Contracts.CourseBuilding;

namespace studyhub.application.Interfaces;

public interface IOnlineCourseOperationsService
{
    Task<OnlineCourseRuntimeState?> GetRuntimeStateAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<OnlineCourseStageExecutionResult> RetryStageAsync(Guid courseId, OnlineCourseOperationalStage stage, CancellationToken cancellationToken = default);
}
