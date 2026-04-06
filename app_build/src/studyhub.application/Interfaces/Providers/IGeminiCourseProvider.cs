using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.domain.AIContracts;

namespace studyhub.application.Interfaces.Providers;

public interface IGeminiCourseProvider
{
    Task<OnlineCoursePlanningResponse> PlanOnlineCourseAsync(OnlineCoursePlanningRequest request, CancellationToken cancellationToken = default);
    Task<CoursePresentationResponseContract> GenerateCoursePresentationAsync(CoursePresentationRequestContract request, CancellationToken cancellationToken = default);
    Task<CourseRoadmapResponseContract> GenerateRoadmapAsync(CourseRoadmapRequestContract request, CancellationToken cancellationToken = default);
    Task<CourseTextRefinementResponse> RefineCourseTextAsync(CourseTextRefinementRequest request, CancellationToken cancellationToken = default);
    Task<AuxiliaryTextTaskResponse> ExecuteAuxiliaryTaskAsync(AuxiliaryTextTaskRequest request, CancellationToken cancellationToken = default);
    Task<CourseSupplementaryMaterialsResponseContract> GenerateSupplementaryQueriesAsync(CourseSupplementaryMaterialsRequestContract request, CancellationToken cancellationToken = default);
    Task<ProviderValidationResponse> ValidateApiKeyAsync(ProviderValidationRequest request, CancellationToken cancellationToken = default);
}
