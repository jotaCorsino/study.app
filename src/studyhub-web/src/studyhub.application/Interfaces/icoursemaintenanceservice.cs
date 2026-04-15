using studyhub.application.Contracts.Maintenance;

namespace studyhub.application.Interfaces;

public interface ICourseMaintenanceService
{
    Task<CourseMaintenanceOperationResult> RegeneratePresentationAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseMaintenanceOperationResult> RegenerateTextRefinementAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseMaintenanceOperationResult> RegenerateSupplementaryMaterialsAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseMaintenanceOperationResult> RevalidateOnlineCourseAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseMaintenanceOperationResult> ResyncLocalCourseAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseMaintenanceOperationResult> ClearBrokenOperationalStateAsync(Guid courseId, CancellationToken cancellationToken = default);
}
