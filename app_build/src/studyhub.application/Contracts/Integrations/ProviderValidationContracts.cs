namespace studyhub.application.Contracts.Integrations;

public class ProviderValidationRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class ProviderValidationResponse
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}
