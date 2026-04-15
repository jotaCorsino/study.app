using studyhub.application.Contracts.CourseBuilding;

namespace studyhub.application.Interfaces;

public interface IOnlineCuratedCourseBuilder
{
    Task<OnlineCuratedCourseBlueprint> BuildBlueprintAsync(OnlineCuratedCourseBuildRequest request, CancellationToken cancellationToken = default);
    Task<OnlineCuratedCourseBuildResult> BuildCourseAsync(OnlineCourseAssemblyRequest request, CancellationToken cancellationToken = default);
}
