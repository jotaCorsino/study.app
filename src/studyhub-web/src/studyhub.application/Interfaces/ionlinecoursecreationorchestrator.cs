using studyhub.application.Contracts.CourseBuilding;

namespace studyhub.application.Interfaces;

public interface IOnlineCourseCreationOrchestrator
{
    Task<OnlineCourseCreationResult> CreateCourseAsync(OnlineCourseIntentRequest request, CancellationToken cancellationToken = default);
}
