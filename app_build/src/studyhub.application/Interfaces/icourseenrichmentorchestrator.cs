using studyhub.application.Contracts.Integrations;

namespace studyhub.application.Interfaces;

public interface ICourseEnrichmentOrchestrator
{
    Task<LocalCourseEnrichmentResponse> EnrichLocalCourseAsync(LocalCourseEnrichmentRequest request, CancellationToken cancellationToken = default);
}
