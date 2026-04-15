using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Contracts.Settings;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;

namespace studyhub.infrastructure.services;

public class ProviderValidationService(
    IGeminiCourseProvider geminiCourseProvider,
    IYouTubeDiscoveryProvider youTubeDiscoveryProvider,
    ILogger<ProviderValidationService> logger) : IProviderValidationService
{
    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly IYouTubeDiscoveryProvider _youTubeDiscoveryProvider = youTubeDiscoveryProvider;
    private readonly ILogger<ProviderValidationService> _logger = logger;

    public async Task<ProviderValidationResponse> ValidateAsync(IntegrationProviderKind provider, string apiKey, CancellationToken cancellationToken = default)
    {
        var request = new ProviderValidationRequest
        {
            ProviderName = provider.ToString(),
            ApiKey = apiKey
        };

        _logger.LogInformation("Provider validation started. Provider: {Provider}", provider);

        var response = provider switch
        {
            IntegrationProviderKind.Gemini => await _geminiCourseProvider.ValidateApiKeyAsync(request, cancellationToken),
            IntegrationProviderKind.YouTube => await _youTubeDiscoveryProvider.ValidateApiKeyAsync(request, cancellationToken),
            _ => new ProviderValidationResponse
            {
                IsValid = false,
                Message = "Provider nao suportado."
            }
        };

        _logger.LogInformation(
            "Provider validation completed. Provider: {Provider}. IsValid: {IsValid}",
            provider,
            response.IsValid);

        return response;
    }
}
