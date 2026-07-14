using studyhub.application.Contracts.CourseSourceManagement;

namespace studyhub.application.Interfaces;

public interface ICourseSourceManagementService
{
    Task<CourseSourceStatusResult> GetSourceStatusAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<CourseSourceLocationValidationResult> ValidateLocationAsync(
        Guid courseId,
        string folderPath,
        CancellationToken cancellationToken = default);

    Task<CourseSourceLocationChangeResult> ChangeLocationAsync(
        ChangeCourseSourceLocationRequest request,
        CancellationToken cancellationToken = default);
}
