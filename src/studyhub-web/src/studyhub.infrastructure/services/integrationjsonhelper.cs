using System.Text.Json;
using System.Text.Json.Serialization;

namespace studyhub.infrastructure.services;

internal static class IntegrationJsonHelper
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize(object? value)
        => value == null ? string.Empty : JsonSerializer.Serialize(value, JsonOptions);

    public static T Deserialize<T>(string rawContent)
    {
        var normalized = ExtractJson(rawContent);
        var result = JsonSerializer.Deserialize<T>(normalized, JsonOptions);

        return result ?? throw new InvalidOperationException($"Nao foi possivel desserializar o payload para {typeof(T).Name}.");
    }

    public static string ExtractJson(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new InvalidOperationException("O provider retornou um payload vazio.");
        }

        var trimmed = rawContent.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].Trim();
            }
        }

        var firstObject = trimmed.IndexOf('{');
        var firstArray = trimmed.IndexOf('[');
        var first = firstObject >= 0 && firstArray >= 0
            ? Math.Min(firstObject, firstArray)
            : Math.Max(firstObject, firstArray);

        if (first < 0)
        {
            return trimmed;
        }

        var lastObject = trimmed.LastIndexOf('}');
        var lastArray = trimmed.LastIndexOf(']');
        var last = Math.Max(lastObject, lastArray);

        return last > first
            ? trimmed.Substring(first, last - first + 1)
            : trimmed[first..];
    }
}
