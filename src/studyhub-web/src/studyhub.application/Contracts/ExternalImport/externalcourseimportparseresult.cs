namespace studyhub.application.Contracts.ExternalImport;

public sealed class ExternalCourseImportParseResult
{
    public bool Success { get; init; }
    public ExternalCourseImportParseErrorKind ErrorKind { get; init; } = ExternalCourseImportParseErrorKind.None;
    public string Message { get; init; } = string.Empty;
    public ExternalCourseImportDocument? Document { get; init; }
    public string NormalizedSchemaVersion { get; init; } = string.Empty;
    public string PayloadFingerprint { get; init; } = string.Empty;

    public static ExternalCourseImportParseResult Successful(
        ExternalCourseImportDocument document,
        string normalizedSchemaVersion,
        string payloadFingerprint)
        => new()
        {
            Success = true,
            Document = document,
            NormalizedSchemaVersion = normalizedSchemaVersion,
            PayloadFingerprint = payloadFingerprint
        };

    public static ExternalCourseImportParseResult Failed(ExternalCourseImportParseErrorKind errorKind, string message)
        => new()
        {
            Success = false,
            ErrorKind = errorKind,
            Message = message
        };
}

public enum ExternalCourseImportParseErrorKind
{
    None = 0,
    EmptyPayload = 1,
    InvalidJson = 2,
    UnsupportedSchemaVersion = 3,
    MissingRequiredData = 4
}
