using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using studyhub.application.Contracts.ExternalImport;
using studyhub.application.Interfaces;

namespace studyhub.infrastructure.services;

public sealed class ExternalCourseJsonParser : IExternalCourseJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Regex SchemaVersionRegex = new(
        "^(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ExternalCourseImportParseResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ExternalCourseImportParseResult.Failed(
                ExternalCourseImportParseErrorKind.EmptyPayload,
                "O payload JSON externo esta vazio.");
        }

        ExternalCourseImportDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ExternalCourseImportDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return ExternalCourseImportParseResult.Failed(
                ExternalCourseImportParseErrorKind.InvalidJson,
                "O payload JSON externo nao e valido.");
        }

        if (document == null)
        {
            return ExternalCourseImportParseResult.Failed(
                ExternalCourseImportParseErrorKind.InvalidJson,
                "O payload JSON externo nao gerou um documento utilizavel.");
        }

        NormalizeDocument(document);

        if (!TryNormalizeSchemaVersion(document.SchemaVersion, out var normalizedSchemaVersion))
        {
            return ExternalCourseImportParseResult.Failed(
                ExternalCourseImportParseErrorKind.UnsupportedSchemaVersion,
                "O StudyHub aceita apenas payloads de importacao externa com schemaVersion 1.x.x.");
        }

        var validationMessage = ValidateRequiredFields(document);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return ExternalCourseImportParseResult.Failed(
                ExternalCourseImportParseErrorKind.MissingRequiredData,
                validationMessage);
        }

        var payloadFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json.Trim())));
        return ExternalCourseImportParseResult.Successful(document, normalizedSchemaVersion, payloadFingerprint);
    }

    private static bool TryNormalizeSchemaVersion(string? schemaVersion, out string normalizedSchemaVersion)
    {
        normalizedSchemaVersion = string.Empty;
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return false;
        }

        var match = SchemaVersionRegex.Match(schemaVersion.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, out var major) || major != 1)
        {
            return false;
        }

        normalizedSchemaVersion = $"{major}.{match.Groups["minor"].Value}.{match.Groups["patch"].Value}";
        return true;
    }

    private static string ValidateRequiredFields(ExternalCourseImportDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Source.Provider))
        {
            return "O payload externo precisa informar source.provider.";
        }

        if (string.IsNullOrWhiteSpace(document.Course.ExternalId))
        {
            return "O payload externo precisa informar course.externalId.";
        }

        if (string.IsNullOrWhiteSpace(document.Course.Title))
        {
            return "O payload externo precisa informar course.title.";
        }

        if (document.Disciplines.Count == 0)
        {
            return "O payload externo precisa informar ao menos uma disciplina em disciplines.";
        }

        foreach (var discipline in document.Disciplines)
        {
            if (string.IsNullOrWhiteSpace(discipline.ExternalId))
            {
                return "Cada disciplina precisa informar externalId.";
            }

            if (string.IsNullOrWhiteSpace(discipline.Title))
            {
                return "Cada disciplina precisa informar title.";
            }

            foreach (var module in discipline.Modules)
            {
                if (string.IsNullOrWhiteSpace(module.ExternalId))
                {
                    return $"O modulo da disciplina '{discipline.Title}' precisa informar externalId.";
                }

                if (module.Order <= 0)
                {
                    return $"O modulo '{module.Title}' da disciplina '{discipline.Title}' precisa informar order > 0.";
                }

                if (string.IsNullOrWhiteSpace(module.Title))
                {
                    return $"O modulo da disciplina '{discipline.Title}' precisa informar title.";
                }

                foreach (var lesson in module.Lessons)
                {
                    if (string.IsNullOrWhiteSpace(lesson.ExternalId))
                    {
                        return $"A aula do modulo '{module.Title}' precisa informar externalId.";
                    }

                    if (lesson.Order <= 0)
                    {
                        return $"A aula '{lesson.Title}' do modulo '{module.Title}' precisa informar order > 0.";
                    }

                    if (string.IsNullOrWhiteSpace(lesson.Title))
                    {
                        return $"A aula do modulo '{module.Title}' precisa informar title.";
                    }
                }
            }
        }

        return string.Empty;
    }

    private static void NormalizeDocument(ExternalCourseImportDocument document)
    {
        document.Source ??= new ExternalCourseImportSource();
        document.Course ??= new ExternalCourseImportCourse();
        document.Disciplines ??= [];

        foreach (var discipline in document.Disciplines)
        {
            discipline.Period ??= new ExternalCourseImportPeriod();
            discipline.Modules ??= [];
            discipline.Assessments ??= [];
            discipline.Metadata ??= [];

            foreach (var module in discipline.Modules)
            {
                module.Lessons ??= [];
                module.Metadata ??= [];

                foreach (var lesson in module.Lessons)
                {
                    lesson.Progress ??= new ExternalCourseImportProgress();
                    lesson.Source ??= new ExternalCourseImportLessonSource();
                    lesson.Metadata ??= [];
                }
            }

            foreach (var assessment in discipline.Assessments)
            {
                assessment.Availability ??= new ExternalCourseImportAvailability();
                assessment.Metadata ??= [];
            }
        }
    }
}
