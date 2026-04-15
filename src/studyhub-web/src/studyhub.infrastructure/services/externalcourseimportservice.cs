using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.ExternalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.shared.Enums;

namespace studyhub.infrastructure.services;

public sealed class ExternalCourseImportService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IExternalCourseJsonParser parser,
    ILogger<ExternalCourseImportService> logger) : IExternalCourseImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IExternalCourseJsonParser _parser = parser;
    private readonly ILogger<ExternalCourseImportService> _logger = logger;

    public async Task<ExternalCourseImportResult> ImportFromJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        var parseResult = _parser.Parse(json);
        if (!parseResult.Success || parseResult.Document == null)
        {
            return MapParseFailure(parseResult);
        }

        var document = parseResult.Document;
        var provider = FirstNonEmpty(document.Source.Provider, document.Course.Provider, "ExternalImport");
        var courseId = CourseIdentityHelper.CreateExternalCourseId(provider, document.Course.ExternalId);

        _logger.LogInformation(
            "External course import started. CourseId: {CourseId}. Provider: {Provider}. SchemaVersion: {SchemaVersion}",
            courseId,
            provider,
            parseResult.NormalizedSchemaVersion);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var existingCourse = await context.Courses
                .AsNoTracking()
                .AnyAsync(course => course.Id == courseId, cancellationToken);

            var course = BuildCourse(
                document,
                provider,
                courseId,
                parseResult.NormalizedSchemaVersion,
                parseResult.PayloadFingerprint,
                out var counts,
                out var assessmentRecords);

            if (counts.LessonCount == 0)
            {
                return ExternalCourseImportResult.Failed(
                    "O payload externo nao contem aulas reproduziveis pelo runtime atual do StudyHub.",
                    ExternalCourseImportErrorKind.NoSupportedLessons);
            }

            await CoursePersistenceHelper.UpsertCourseAsync(context, course, cancellationToken);
            await SaveExternalImportSnapshotAsync(context, courseId, document, parseResult, json, cancellationToken);
            await ReplaceExternalAssessmentsAsync(context, courseId, assessmentRecords, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "External course import completed. CourseId: {CourseId}. Status: {Status}. Disciplines: {DisciplineCount}. Modules: {ModuleCount}. Lessons: {LessonCount}. Assessments: {AssessmentCount}. SkippedLessons: {SkippedLessonCount}",
                courseId,
                existingCourse ? ExternalCourseImportStatus.Updated : ExternalCourseImportStatus.Imported,
                counts.DisciplineCount,
                counts.ModuleCount,
                counts.LessonCount,
                counts.AssessmentCount,
                counts.SkippedLessonCount);

            return new ExternalCourseImportResult
            {
                Status = existingCourse ? ExternalCourseImportStatus.Updated : ExternalCourseImportStatus.Imported,
                CourseId = courseId,
                CourseTitle = course.Title,
                Provider = provider,
                SchemaVersion = parseResult.NormalizedSchemaVersion,
                DisciplineCount = counts.DisciplineCount,
                ModuleCount = counts.ModuleCount,
                LessonCount = counts.LessonCount,
                SkippedLessonCount = counts.SkippedLessonCount,
                AssessmentCount = counts.AssessmentCount,
                Message = counts.SkippedLessonCount > 0
                    ? "Curso externo importado com sucesso; itens nao reproduziveis ficaram preservados apenas no snapshot bruto para futuras evolucoes."
                    : "Curso externo importado com sucesso."
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update failed while importing external course {CourseId}.", courseId);
            return ExternalCourseImportResult.Failed(
                "Erro ao persistir o curso externo importado.",
                ExternalCourseImportErrorKind.PersistenceFailed);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Entity tracking failed while importing external course {CourseId}.", courseId);
            return ExternalCourseImportResult.Failed(
                "Erro ao atualizar os dados estruturais do curso externo.",
                ExternalCourseImportErrorKind.PersistenceFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while importing external course {CourseId}.", courseId);
            return ExternalCourseImportResult.Failed(
                "Ocorreu um erro inesperado ao importar o curso externo.",
                ExternalCourseImportErrorKind.Unexpected);
        }
    }

    private static Course BuildCourse(
        ExternalCourseImportDocument document,
        string provider,
        Guid courseId,
        string normalizedSchemaVersion,
        string payloadFingerprint,
        out ExternalImportCounts counts,
        out List<ExternalAssessmentRecord> assessmentRecords)
    {
        var disciplineCount = document.Disciplines.Count;
        var externalModuleCount = 0;
        var importedLessonCount = 0;
        var skippedLessonCount = 0;
        var sourceUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modules = new List<Module>();
        assessmentRecords = [];

        if (!string.IsNullOrWhiteSpace(document.Source.OriginUrl))
        {
            sourceUrls.Add(document.Source.OriginUrl.Trim());
        }

        foreach (var discipline in document.Disciplines)
        {
            var disciplineKey = FirstNonEmpty(discipline.ExternalId, discipline.Code, discipline.Title);
            var moduleId = CourseIdentityHelper.CreateExternalDisciplineModuleId(courseId, disciplineKey);
            var topics = new List<Topic>();

            foreach (var externalModule in discipline.Modules.OrderBy(module => module.Order))
            {
                externalModuleCount++;
                var moduleKey = FirstNonEmpty(externalModule.ExternalId, externalModule.Title, externalModule.Order.ToString());
                var topicId = CourseIdentityHelper.CreateExternalTopicId(moduleId, moduleKey);
                var lessons = new List<Lesson>();

                foreach (var externalLesson in externalModule.Lessons.OrderBy(lesson => lesson.Order))
                {
                    var lesson = TryBuildLesson(
                        provider,
                        courseId,
                        disciplineKey,
                        moduleKey,
                        topicId,
                        externalLesson,
                        out var lessonUrl);

                    if (lesson == null)
                    {
                        skippedLessonCount++;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(lessonUrl))
                    {
                        sourceUrls.Add(lessonUrl);
                    }

                    importedLessonCount++;
                    lessons.Add(lesson);
                }

                if (lessons.Count == 0)
                {
                    continue;
                }

                topics.Add(new Topic
                {
                    Id = topicId,
                    ModuleId = moduleId,
                    Order = topics.Count + 1,
                    RawTitle = externalModule.Title.Trim(),
                    RawDescription = NormalizeText(externalModule.Description),
                    Title = externalModule.Title.Trim(),
                    Description = NormalizeText(externalModule.Description),
                    Lessons = lessons
                });
            }

            assessmentRecords.AddRange(
                BuildAssessmentRecords(courseId, disciplineKey, discipline.Assessments, discipline.ExternalId));

            if (topics.Count == 0)
            {
                continue;
            }

            modules.Add(new Module
            {
                Id = moduleId,
                CourseId = courseId,
                Order = modules.Count + 1,
                RawTitle = discipline.Title.Trim(),
                RawDescription = NormalizeText(discipline.Description),
                Title = discipline.Title.Trim(),
                Description = NormalizeText(discipline.Description),
                Topics = topics
            });
        }

        counts = new ExternalImportCounts(
            disciplineCount,
            externalModuleCount,
            importedLessonCount,
            skippedLessonCount,
            assessmentRecords.Count);

        var completedSteps = new List<string>
        {
            "ExternalJsonImported",
            "ExternalStructurePersisted"
        };

        if (assessmentRecords.Count > 0)
        {
            completedSteps.Add("ExternalAssessmentsPersisted");
        }

        return new Course
        {
            Id = courseId,
            RawTitle = document.Course.Title.Trim(),
            RawDescription = NormalizeText(document.Course.Description),
            Title = document.Course.Title.Trim(),
            Description = NormalizeText(document.Course.Description),
            Category = FirstNonEmpty(document.Course.Category, "Curso Externo"),
            ThumbnailUrl = FirstNonEmpty(document.Course.ThumbnailUrl, document.Course.CoverImageUrl),
            SourceType = CourseSourceType.ExternalImport,
            SourceMetadata = new CourseSourceMetadata
            {
                ImportedAt = document.Source.ExportedAt ?? DateTime.UtcNow,
                ScanVersion = $"external-import:{normalizedSchemaVersion}",
                Provider = provider,
                SourceUrls = sourceUrls.ToList(),
                CompletedSteps = completedSteps,
                GenerationSummary = "Curso externo importado a partir de payload JSON versionado.",
                ExternalSystem = NormalizeText(document.Source.System),
                ExternalCourseId = document.Course.ExternalId,
                ExternalCourseSlug = NormalizeText(document.Course.Slug),
                ExternalDisciplineIds = document.Disciplines
                    .Select(discipline => discipline.ExternalId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ImportPayloadFingerprint = payloadFingerprint,
                ImportSchemaVersion = normalizedSchemaVersion,
                ImportSourceKind = NormalizeText(document.Source.Kind)
            },
            TotalDuration = TimeSpan.FromTicks(modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Sum(lesson => lesson.Duration.Ticks)),
            AddedAt = DateTime.Now,
            Modules = modules
        };
    }

    private static Lesson? TryBuildLesson(
        string provider,
        Guid courseId,
        string disciplineKey,
        string moduleKey,
        Guid topicId,
        ExternalCourseImportLesson sourceLesson,
        out string lessonUrl)
    {
        lessonUrl = string.Empty;

        var lessonSource = sourceLesson.Source ?? new ExternalCourseImportLessonSource();
        var lessonProgress = sourceLesson.Progress ?? new ExternalCourseImportProgress();

        var localFilePath = lessonSource.FilePath?.Trim() ?? string.Empty;
        var externalUrl = lessonSource.Url?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(localFilePath) && string.IsNullOrWhiteSpace(externalUrl))
        {
            return null;
        }

        var lessonKey = FirstNonEmpty(
            sourceLesson.ExternalId,
            lessonSource.ExternalRef,
            externalUrl,
            localFilePath,
            sourceLesson.Title,
            sourceLesson.Order.ToString());

        var durationSeconds = Math.Max(0, sourceLesson.DurationSeconds ?? 0);
        var lastPositionSeconds = Math.Max(0, lessonProgress.LastPositionSeconds ?? 0);
        if (durationSeconds > 0)
        {
            lastPositionSeconds = Math.Min(lastPositionSeconds, durationSeconds);
        }

        var watchedPercentage = ClampPercentage(lessonProgress.WatchedPercentage ?? 0);
        var status = MapLessonStatus(sourceLesson.Status, watchedPercentage);
        if (status == LessonStatus.Completed && watchedPercentage < 100)
        {
            watchedPercentage = 100;
        }

        var lessonId = CourseIdentityHelper.CreateExternalLessonId(
            courseId,
            disciplineKey,
            moduleKey,
            lessonKey);

        var normalizedProvider = FirstNonEmpty(lessonSource.Provider, provider);
        if (!string.IsNullOrWhiteSpace(externalUrl))
        {
            lessonUrl = externalUrl;
        }

        return new Lesson
        {
            Id = lessonId,
            TopicId = topicId,
            Order = sourceLesson.Order,
            RawTitle = sourceLesson.Title.Trim(),
            RawDescription = NormalizeText(sourceLesson.Description),
            Title = sourceLesson.Title.Trim(),
            Description = NormalizeText(sourceLesson.Description),
            SourceType = string.IsNullOrWhiteSpace(localFilePath)
                ? LessonSourceType.ExternalVideo
                : LessonSourceType.LocalFile,
            LocalFilePath = localFilePath,
            ExternalUrl = externalUrl,
            Provider = normalizedProvider,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            Status = status,
            WatchedPercentage = watchedPercentage,
            LastPlaybackPosition = TimeSpan.FromSeconds(lastPositionSeconds)
        };
    }

    private static List<ExternalAssessmentRecord> BuildAssessmentRecords(
        Guid courseId,
        string disciplineKey,
        IReadOnlyList<ExternalCourseImportAssessment> assessments,
        string disciplineExternalId)
    {
        var records = new List<ExternalAssessmentRecord>();
        var orderedAssessments = assessments
            .OrderBy(assessment => (assessment.Availability ?? new ExternalCourseImportAvailability()).StartAt ?? DateTime.MaxValue)
            .ThenBy(assessment => assessment.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < orderedAssessments.Count; index++)
        {
            var assessment = orderedAssessments[index];
            var assessmentKey = FirstNonEmpty(assessment.ExternalId, assessment.Title, (index + 1).ToString());
            var availability = assessment.Availability ?? new ExternalCourseImportAvailability();

            records.Add(new ExternalAssessmentRecord
            {
                Id = CourseIdentityHelper.CreateExternalAssessmentId(courseId, disciplineKey, assessmentKey),
                CourseId = courseId,
                DisciplineExternalId = disciplineExternalId,
                AssessmentExternalId = NormalizeText(assessment.ExternalId),
                AssessmentType = NormalizeText(assessment.Type),
                Title = FirstNonEmpty(assessment.Title, "Avaliacao externa"),
                Description = NormalizeText(assessment.Description),
                Status = NormalizeText(assessment.Status),
                WeightPercentage = assessment.WeightPercentage,
                AvailabilityStartAt = availability.StartAt,
                AvailabilityEndAt = availability.EndAt,
                Grade = assessment.Grade,
                MetadataJson = JsonSerializer.Serialize(assessment.Metadata, JsonOptions),
                UpdatedAt = DateTime.UtcNow
            });
        }

        return records;
    }

    private static ExternalCourseImportResult MapParseFailure(ExternalCourseImportParseResult parseResult)
    {
        var errorKind = parseResult.ErrorKind switch
        {
            ExternalCourseImportParseErrorKind.UnsupportedSchemaVersion => ExternalCourseImportErrorKind.UnsupportedSchemaVersion,
            ExternalCourseImportParseErrorKind.MissingRequiredData => ExternalCourseImportErrorKind.MissingRequiredData,
            _ => ExternalCourseImportErrorKind.InvalidPayload
        };

        return ExternalCourseImportResult.Failed(parseResult.Message, errorKind);
    }

    private static LessonStatus MapLessonStatus(string? status, double watchedPercentage)
    {
        if (watchedPercentage >= 99.5)
        {
            return LessonStatus.Completed;
        }

        return status?.Trim().ToLowerInvariant() switch
        {
            "completed" => LessonStatus.Completed,
            "in-progress" => LessonStatus.InProgress,
            "not-started" => LessonStatus.NotStarted,
            _ when watchedPercentage > 0 => LessonStatus.InProgress,
            _ => LessonStatus.NotStarted
        };
    }

    private static double ClampPercentage(double value)
        => Math.Clamp(value, 0, 100);

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeText(string? value)
        => value?.Trim() ?? string.Empty;

    private static async Task SaveExternalImportSnapshotAsync(
        StudyHubDbContext context,
        Guid courseId,
        ExternalCourseImportDocument document,
        ExternalCourseImportParseResult parseResult,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var snapshot = await context.ExternalCourseImports
            .FirstOrDefaultAsync(item => item.CourseId == courseId, cancellationToken);

        if (snapshot == null)
        {
            snapshot = new ExternalCourseImportRecord
            {
                CourseId = courseId
            };

            await context.ExternalCourseImports.AddAsync(snapshot, cancellationToken);
        }

        snapshot.SchemaVersion = parseResult.NormalizedSchemaVersion;
        snapshot.SourceKind = NormalizeText(document.Source.Kind);
        snapshot.SourceSystem = NormalizeText(document.Source.System);
        snapshot.Provider = FirstNonEmpty(document.Source.Provider, document.Course.Provider);
        snapshot.ExternalCourseId = document.Course.ExternalId;
        snapshot.PayloadFingerprint = parseResult.PayloadFingerprint;
        snapshot.OriginUrl = document.Source.OriginUrl?.Trim() ?? string.Empty;
        snapshot.PayloadJson = payloadJson;
        snapshot.ImportedAt = DateTime.UtcNow;
    }

    private static async Task ReplaceExternalAssessmentsAsync(
        StudyHubDbContext context,
        Guid courseId,
        IReadOnlyCollection<ExternalAssessmentRecord> assessmentRecords,
        CancellationToken cancellationToken)
    {
        var existingRecords = await context.ExternalAssessments
            .Where(item => item.CourseId == courseId)
            .ToListAsync(cancellationToken);

        if (existingRecords.Count > 0)
        {
            context.ExternalAssessments.RemoveRange(existingRecords);
        }

        if (assessmentRecords.Count > 0)
        {
            await context.ExternalAssessments.AddRangeAsync(assessmentRecords, cancellationToken);
        }
    }

    private sealed record ExternalImportCounts(
        int DisciplineCount,
        int ModuleCount,
        int LessonCount,
        int SkippedLessonCount,
        int AssessmentCount);
}
