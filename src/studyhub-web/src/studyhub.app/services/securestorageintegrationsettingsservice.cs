using studyhub.application.Contracts.Settings;
using studyhub.application.Interfaces;
using Microsoft.Extensions.Logging;

namespace studyhub.app.services;

public sealed class SecureStorageIntegrationSettingsService(ILogger<SecureStorageIntegrationSettingsService> logger) : IIntegrationSettingsService
{
    private const string GeminiApiKeyStorageKey = "studyhub.integration.gemini_api_key";
    private const string YouTubeApiKeyStorageKey = "studyhub.integration.youtube_api_key";
    private readonly ILogger<SecureStorageIntegrationSettingsService> _logger = logger;

    public async Task<IntegrationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new IntegrationSettings
        {
            GeminiApiKey = await ReadAsync(GeminiApiKeyStorageKey),
            YouTubeApiKey = await ReadAsync(YouTubeApiKeyStorageKey)
        };

        _logger.LogInformation(
            "Integration settings loaded from SecureStorage. HasGeminiKey: {HasGeminiKey}. HasYouTubeKey: {HasYouTubeKey}",
            settings.HasGeminiKey,
            settings.HasYouTubeKey);

        return settings;
    }

    public async Task SaveSettingsAsync(IntegrationSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await WriteAsync(GeminiApiKeyStorageKey, settings.GeminiApiKey);
        await WriteAsync(YouTubeApiKeyStorageKey, settings.YouTubeApiKey);

        _logger.LogInformation(
            "Integration settings saved to SecureStorage. HasGeminiKey: {HasGeminiKey}. HasYouTubeKey: {HasYouTubeKey}",
            !string.IsNullOrWhiteSpace(settings.GeminiApiKey),
            !string.IsNullOrWhiteSpace(settings.YouTubeApiKey));
    }

    private async Task<string> ReadAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage read failed for key {StorageKey}.", key);
            return string.Empty;
        }
    }

    private async Task WriteAsync(string key, string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                SecureStorage.Default.Remove(key);
                return;
            }

            await SecureStorage.Default.SetAsync(key, value.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage write failed for key {StorageKey}.", key);
            throw;
        }
    }
}
