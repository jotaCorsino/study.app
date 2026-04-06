using studyhub.application.Contracts.Integrations;
using studyhub.application.Contracts.Settings;

namespace studyhub.application.Interfaces;

public interface IProviderValidationService
{
    Task<ProviderValidationResponse> ValidateAsync(IntegrationProviderKind provider, string apiKey, CancellationToken cancellationToken = default);
}
