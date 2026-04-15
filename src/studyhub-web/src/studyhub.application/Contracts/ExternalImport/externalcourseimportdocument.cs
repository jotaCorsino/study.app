using System.Text.Json;
using System.Text.Json.Serialization;

namespace studyhub.application.Contracts.ExternalImport;

public sealed class ExternalCourseImportDocument
{
    public string SchemaVersion { get; set; } = string.Empty;
    public ExternalCourseImportSource Source { get; set; } = new();
    public ExternalCourseImportCourse Course { get; set; } = new();
    public List<ExternalCourseImportDiscipline> Disciplines { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportSource
{
    public string Kind { get; set; } = string.Empty;
    public string System { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderVersion { get; set; } = string.Empty;
    public DateTime? ExportedAt { get; set; }
    public string? OriginUrl { get; set; }
    public string Locale { get; set; } = string.Empty;
    public string PageType { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportCourse
{
    public string ExternalId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string Provider { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportDiscipline
{
    public string ExternalId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public ExternalCourseImportPeriod Period { get; set; } = new();
    public List<ExternalCourseImportModule> Modules { get; set; } = [];
    public List<ExternalCourseImportAssessment> Assessments { get; set; } = [];
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportPeriod
{
    public string Label { get; set; } = string.Empty;
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportModule
{
    public string ExternalId { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ExternalCourseImportLesson> Lessons { get; set; } = [];
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportLesson
{
    public string ExternalId { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DurationSeconds { get; set; }
    public ExternalCourseImportProgress Progress { get; set; } = new();
    public ExternalCourseImportLessonSource Source { get; set; } = new();
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportProgress
{
    public double? WatchedPercentage { get; set; }
    public int? LastPositionSeconds { get; set; }
    public DateTime? CompletedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportLessonSource
{
    public string Kind { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public string? ExternalRef { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportAssessment
{
    public string ExternalId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? WeightPercentage { get; set; }
    public ExternalCourseImportAvailability Availability { get; set; } = new();
    public double? Grade { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ExternalCourseImportAvailability
{
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
