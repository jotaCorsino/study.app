using studyhub.application.Contracts.CourseSourceManagement;

namespace studyhub.application.Interfaces;

public interface ICourseSourceManagementService
{
    Task<CourseSourceLocationValidationResult> ValidateLocationAsync(
        Guid courseId,
        string folderPath,
        CancellationToken cancellationToken = default);

    Task<CourseSourceLocationChangeResult> ChangeLocationAsync(
        ChangeCourseSourceLocationRequest request,
        CancellationToken cancellationToken = default);
}
