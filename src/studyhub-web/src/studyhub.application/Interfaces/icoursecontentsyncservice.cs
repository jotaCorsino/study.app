using studyhub.application.Contracts.CourseContentSync;

namespace studyhub.application.Interfaces;

public interface ICourseContentSyncService
{
    Task<CourseContentSyncPreviewResult> PreviewAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);
}
