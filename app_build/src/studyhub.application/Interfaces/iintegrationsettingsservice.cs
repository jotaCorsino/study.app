using studyhub.application.Contracts.Settings;

namespace studyhub.application.Interfaces;

public interface IIntegrationSettingsService
{
    Task<IntegrationSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(IntegrationSettings settings, CancellationToken cancellationToken = default);
}
