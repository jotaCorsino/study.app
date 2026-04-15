using studyhub.application.Contracts.CourseBuilding;

namespace studyhub.application.Interfaces;

public interface ILocalFolderCourseBuilder
{
    Task<LocalFolderCourseBuildResult> BuildAsync(LocalFolderCourseBuildRequest request, CancellationToken cancellationToken = default);
}
