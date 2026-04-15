using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;

namespace studyhub.application.Interfaces.Providers;

public interface IYouTubeDiscoveryProvider
{
    Task<YouTubeCourseDiscoveryResponse> DiscoverCourseSourcesAsync(YouTubeCourseDiscoveryRequest request, CancellationToken cancellationToken = default);
    Task<YoutubeMaterialDiscoveryResponse> DiscoverMaterialsAsync(YoutubeMaterialDiscoveryRequest request, CancellationToken cancellationToken = default);
    Task<ProviderValidationResponse> ValidateApiKeyAsync(ProviderValidationRequest request, CancellationToken cancellationToken = default);
}
