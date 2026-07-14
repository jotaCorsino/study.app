using System.Text.Json;
using System.Text.Json.Nodes;

namespace studyhub.infrastructure.services;

internal static class LocalCourseSourceRootResolver
{
    public static bool TryResolve(
        string? sourceMetadataJson,
        string? folderPath,
        out string normalizedRootPath)
    {
        if (TryReadMetadataRootPath(sourceMetadataJson, out var metadataRootPath) &&
            TryNormalize(metadataRootPath, out normalizedRootPath))
        {
            return true;
        }

        return TryNormalize(folderPath, out normalizedRootPath);
    }

    public static bool TryNormalize(string? path, out string normalizedRootPath)
    {
        normalizedRootPath = string.Empty;
        if (!LocalLessonPathHelper.TryNormalizeFullyQualifiedPath(path, out var fullyQualifiedPath))
        {
            return false;
        }

        normalizedRootPath = Path.TrimEndingDirectorySeparator(fullyQualifiedPath);
        return true;
    }

    private static bool TryReadMetadataRootPath(string? metadataJson, out string rootPath)
    {
        rootPath = string.Empty;
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(metadataJson) is not JsonObject root)
            {
                return false;
            }

            var propertyName = root
                .Select(property => property.Key)
                .FirstOrDefault(name => string.Equals(
                    name,
                    "rootPath",
                    StringComparison.OrdinalIgnoreCase));

            rootPath = propertyName is null
                ? string.Empty
                : root[propertyName]?.GetValue<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(rootPath);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
