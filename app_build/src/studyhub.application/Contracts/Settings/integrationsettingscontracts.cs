namespace studyhub.application.Contracts.Settings;

public sealed class IntegrationSettings
{
    public string GeminiApiKey { get; set; } = string.Empty;
    public string YouTubeApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string YouTubeRegionCode { get; set; } = "US";

    public bool HasGeminiKey => !string.IsNullOrWhiteSpace(GeminiApiKey);
    public bool HasYouTubeKey => !string.IsNullOrWhiteSpace(YouTubeApiKey);
}

public enum IntegrationProviderKind
{
    Gemini = 0,
    YouTube = 1
}
