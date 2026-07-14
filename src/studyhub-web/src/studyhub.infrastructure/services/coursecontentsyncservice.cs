using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseContentSync;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public sealed class CourseContentSyncService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    ILocalCourseScanner localCourseScanner,
    ILogger<CourseContentSyncService> logger) : ICourseContentSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ILocalCourseScanner _localCourseScanner = localCourseScanner;
    private readonly ILogger<CourseContentSyncService> _logger = logger;
    private readonly CourseContentSyncPlanner _planner = new();
    private readonly CourseContentSyncApplier _applier = new();

    public async Task<CourseContentSyncPreviewResult> PreviewAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
        => (await ExecutePreviewAsync(courseId, cancellationToken)).Preview;

    public async Task<CourseContentSyncApplyResult> ApplyAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var execution = await ExecutePreviewAsync(courseId, cancellationToken);
        if (!execution.Preview.Success || execution.DetectedStructure is null)
        {
            return CreateBlockedApplyResult(
                courseId,
                execution.Preview,
                $"A sincronização não pode ser aplicada: {execution.Preview.Message}");
        }

        var effectivePlan = execution.Preview;
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var course = await LoadCourseAsync(
                    context,
                    courseId,
                    tracking: true,
                    cancellationToken);
                if (course is null)
                {
                    await RollbackSafelyAsync(transaction);
                    return CreateBlockedApplyResult(
                        courseId,
                        CreateError(
                            courseId,
                            CourseContentSyncPreviewStatus.CourseNotFound,
                            "O curso não foi encontrado."),
                        "O curso deixou de existir antes da aplicação da sincronização.");
                }

                if (course.SourceType != CourseSourceType.LocalFolder)
                {
                    await RollbackSafelyAsync(transaction);
                    return CreateBlockedApplyResult(
                        courseId,
                        CreateError(
                            courseId,
                            CourseContentSyncPreviewStatus.UnsupportedCourseSource,
                            "A origem desse curso não é uma pasta local.",
                            ResolveCourseTitle(course)),
                        "A origem do curso mudou antes da aplicação da sincronização.");
                }

                if (!LocalCourseSourceRootResolver.TryResolve(
                        course.SourceMetadataJson,
                        course.FolderPath,
                        out var currentRootPath) ||
                    !PathComparer.Equals(currentRootPath, execution.ScannedRootPath))
                {
                    await RollbackSafelyAsync(transaction);
                    return CreateBlockedApplyResult(
                        courseId,
                        execution.Preview,
                        "A origem do curso mudou durante a sincronização. Examine a pasta novamente e tente outra vez.");
                }

                var currentPlan = _planner.Plan(
                    course,
                    execution.DetectedStructure,
                    execution.ScannedRootPath);
                effectivePlan = currentPlan;
                if (!currentPlan.Success)
                {
                    await RollbackSafelyAsync(transaction);
                    return CreateBlockedApplyResult(
                        courseId,
                        currentPlan,
                        $"A estrutura persistida mudou e a sincronização foi bloqueada: {currentPlan.Message}");
                }

                var scannedAtUtc = ResolveScannedAtUtc(
                    currentPlan.ScannedAtUtc,
                    execution.DetectedStructure.ScannedAt);
                if (!TryPatchLastScannedAtUtc(
                        course.SourceMetadataJson,
                        scannedAtUtc,
                        out var updatedSourceMetadataJson))
                {
                    await RollbackSafelyAsync(transaction);
                    return CreateFailedApplyResult(
                        courseId,
                        currentPlan,
                        "Os metadados da origem estão inválidos e não puderam ser preservados com segurança.");
                }

                var summary = _applier.Apply(
                    course,
                    currentPlan,
                    execution.ScannedRootPath);
                context.Modules.AddRange(summary.CreatedModules);
                context.Topics.AddRange(summary.CreatedTopics);
                context.Lessons.AddRange(summary.CreatedLessons);

                var totalDurationMinutes = CalculateKnownDurationMinutes(course);
                if (course.TotalDurationMinutes != totalDurationMinutes)
                {
                    course.TotalDurationMinutes = totalDurationMinutes;
                    summary.ContentChanged = true;
                }

                course.SourceMetadataJson = updatedSourceMetadataJson;

                var manifest = LocalCourseManifestBuilder.Build(
                    course,
                    execution.ScannedRootPath,
                    scannedAtUtc);
                if (!LocalCourseManifestValidator.HasUsableStructure(manifest) ||
                    !LocalCourseManifestValidator.HasMatchingPersistedIdentities(manifest, course))
                {
                    throw new InvalidOperationException(
                        "The rebuilt local course manifest does not match the tracked course tree.");
                }

                var snapshot = await context.CourseImportSnapshots
                    .SingleOrDefaultAsync(record => record.CourseId == courseId, cancellationToken);
                if (snapshot is null)
                {
                    snapshot = new CourseImportSnapshotRecord
                    {
                        CourseId = courseId,
                        SourceKind = "local-folder",
                        ImportedAt = ResolveHistoricalImportedAt(
                            updatedSourceMetadataJson,
                            course.AddedAt,
                            scannedAtUtc)
                    };
                    await context.CourseImportSnapshots.AddAsync(snapshot, cancellationToken);
                }
                else if (string.IsNullOrWhiteSpace(snapshot.SourceKind))
                {
                    snapshot.SourceKind = "local-folder";
                }

                snapshot.RootFolderPath = execution.ScannedRootPath;
                snapshot.StructureJson = JsonSerializer.Serialize(manifest, JsonOptions);

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return CreateSuccessfulApplyResult(courseId, currentPlan, summary);
            }
            catch (OperationCanceledException)
            {
                await RollbackSafelyAsync(transaction);
                throw;
            }
            catch
            {
                await RollbackSafelyAsync(transaction);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not apply the content sync for course {CourseId}; the transaction was rolled back.",
                courseId);
            return CreateFailedApplyResult(
                courseId,
                effectivePlan,
                "Não foi possível salvar a sincronização. Nenhuma alteração parcial foi mantida.");
        }
    }

    private async Task<PreviewExecution> ExecutePreviewAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CourseRecord? course;
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            course = await LoadCourseAsync(context, courseId, tracking: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load course {CourseId} for content sync preview.", courseId);
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Não foi possível carregar o curso para planejar a sincronização."));
        }

        if (course is null)
        {
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.CourseNotFound,
                "O curso não foi encontrado."));
        }

        var courseTitle = ResolveCourseTitle(course);
        if (course.SourceType != CourseSourceType.LocalFolder)
        {
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.UnsupportedCourseSource,
                "A origem desse curso não é uma pasta local.",
                courseTitle), course);
        }

        if (!LocalCourseSourceRootResolver.TryResolve(
                course.SourceMetadataJson,
                course.FolderPath,
                out var normalizedRootPath))
        {
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.SourceUnavailable,
                "A origem atual do curso não possui uma pasta local válida.",
                courseTitle), course);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceProbeError = ProbeSourceDirectory(
            courseId,
            courseTitle,
            normalizedRootPath);
        if (sourceProbeError is not null)
        {
            return PreviewExecution.FromError(sourceProbeError, course, normalizedRootPath);
        }

        DetectedCourseStructure detectedStructure;
        try
        {
            detectedStructure = await _localCourseScanner.ScanAsync(
                normalizedRootPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(
                ex,
                "Access denied while scanning course {CourseId} source {RootPath} for sync preview.",
                courseId,
                normalizedRootPath);
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.AccessDenied,
                "O acesso à pasta atual do curso foi negado.",
                courseTitle,
                normalizedRootPath), course, normalizedRootPath);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            _logger.LogWarning(
                ex,
                "Course {CourseId} source {RootPath} became unavailable during sync preview.",
                courseId,
                normalizedRootPath);
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.SourceUnavailable,
                "A pasta atual do curso não está disponível.",
                courseTitle,
                normalizedRootPath), course, normalizedRootPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "I/O error while scanning course {CourseId} source {RootPath} for sync preview.",
                courseId,
                normalizedRootPath);
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.ScanFailed,
                "Não foi possível ler o conteúdo da pasta atual do curso.",
                courseTitle,
                normalizedRootPath), course, normalizedRootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while scanning course {CourseId} source {RootPath} for sync preview.",
                courseId,
                normalizedRootPath);
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Ocorreu um erro inesperado ao examinar o conteúdo atual do curso.",
                courseTitle,
                normalizedRootPath), course, normalizedRootPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (detectedStructure.LessonCount == 0)
        {
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.NoVideosFound,
                "A pasta atual do curso não contém vídeos reconhecidos.",
                courseTitle,
                normalizedRootPath,
                detectedStructure.ScannedAt), course, normalizedRootPath, detectedStructure);
        }

        try
        {
            var preview = _planner.Plan(course, detectedStructure, normalizedRootPath);
            return new PreviewExecution(course, detectedStructure, normalizedRootPath, preview);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while planning course {CourseId} content sync.",
                courseId);
            return PreviewExecution.FromError(CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Ocorreu um erro inesperado ao planejar a sincronização do curso.",
                courseTitle,
                normalizedRootPath,
                detectedStructure.ScannedAt), course, normalizedRootPath, detectedStructure);
        }
    }

    private CourseContentSyncPreviewResult? ProbeSourceDirectory(
        Guid courseId,
        string courseTitle,
        string normalizedRootPath)
    {
        try
        {
            if (!File.GetAttributes(normalizedRootPath).HasFlag(FileAttributes.Directory))
            {
                return CreateError(
                    courseId,
                    CourseContentSyncPreviewStatus.SourceUnavailable,
                    "A origem atual do curso não é uma pasta disponível.",
                    courseTitle,
                    normalizedRootPath);
            }

            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(
                ex,
                "Access denied while checking course {CourseId} source {RootPath}.",
                courseId,
                normalizedRootPath);
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.AccessDenied,
                "O acesso à pasta atual do curso foi negado.",
                courseTitle,
                normalizedRootPath);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.SourceUnavailable,
                "A pasta atual do curso não está disponível.",
                courseTitle,
                normalizedRootPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "I/O error while checking course {CourseId} source {RootPath}.",
                courseId,
                normalizedRootPath);
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.ScanFailed,
                "Não foi possível acessar a pasta atual do curso.",
                courseTitle,
                normalizedRootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while checking course {CourseId} source {RootPath}.",
                courseId,
                normalizedRootPath);
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Ocorreu um erro inesperado ao verificar a pasta atual do curso.",
                courseTitle,
                normalizedRootPath);
        }
    }

    private static async Task<CourseRecord?> LoadCourseAsync(
        StudyHubDbContext context,
        Guid courseId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = context.Courses
            .AsSplitQuery()
            .Include(record => record.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .Where(record => record.Id == courseId);

        return tracking
            ? await query.SingleOrDefaultAsync(cancellationToken)
            : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    private static bool TryPatchLastScannedAtUtc(
        string? sourceMetadataJson,
        DateTime scannedAtUtc,
        out string updatedSourceMetadataJson)
    {
        updatedSourceMetadataJson = string.Empty;
        try
        {
            JsonObject root;
            if (string.IsNullOrWhiteSpace(sourceMetadataJson))
            {
                root = [];
            }
            else if (JsonNode.Parse(sourceMetadataJson) is JsonObject parsedRoot)
            {
                root = parsedRoot;
            }
            else
            {
                return false;
            }

            var propertyName = root
                .Select(property => property.Key)
                .FirstOrDefault(name => string.Equals(
                    name,
                    "lastScannedAtUtc",
                    StringComparison.OrdinalIgnoreCase)) ?? "lastScannedAtUtc";
            root[propertyName] = scannedAtUtc;
            updatedSourceMetadataJson = root.ToJsonString(JsonOptions);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static DateTime ResolveScannedAtUtc(DateTime? previewScannedAtUtc, DateTime detectedScannedAt)
    {
        var value = previewScannedAtUtc ?? (detectedScannedAt == default
            ? DateTime.UtcNow
            : detectedScannedAt);
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime ResolveHistoricalImportedAt(
        string sourceMetadataJson,
        DateTime addedAt,
        DateTime fallback)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<CourseSourceMetadata>(
                sourceMetadataJson,
                JsonOptions);
            if (metadata?.ImportedAt is DateTime importedAt && importedAt != default)
            {
                return importedAt;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
        }

        return addedAt == default ? fallback : addedAt;
    }

    private static int CalculateKnownDurationMinutes(CourseRecord course)
    {
        var total = course.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Sum(lesson => Math.Max(0L, lesson.DurationMinutes));
        return (int)Math.Min(total, int.MaxValue);
    }

    private static CourseContentSyncApplyResult CreateSuccessfulApplyResult(
        Guid courseId,
        CourseContentSyncPreviewResult preview,
        CourseContentSyncApplySummary summary)
    {
        var appliedStructuralChanges = summary.CreatedModuleCount > 0 ||
                                       summary.CreatedTopicCount > 0 ||
                                       summary.CreatedLessonCount > 0 ||
                                       summary.MarkedMissingModuleCount > 0 ||
                                       summary.MarkedMissingTopicCount > 0 ||
                                       summary.MarkedMissingLessonCount > 0 ||
                                       summary.RestoredAvailableItemCount > 0;
        return new CourseContentSyncApplyResult
        {
            CourseId = courseId,
            Status = appliedStructuralChanges
                ? CourseContentSyncApplyStatus.Applied
                : CourseContentSyncApplyStatus.NoChanges,
            Message = appliedStructuralChanges
                ? "A sincronização do conteúdo foi aplicada com sucesso."
                : "A sincronização foi concluída sem mudanças estruturais.",
            Preview = preview,
            CreatedModuleCount = summary.CreatedModuleCount,
            CreatedTopicCount = summary.CreatedTopicCount,
            CreatedLessonCount = summary.CreatedLessonCount,
            MarkedMissingModuleCount = summary.MarkedMissingModuleCount,
            MarkedMissingTopicCount = summary.MarkedMissingTopicCount,
            MarkedMissingLessonCount = summary.MarkedMissingLessonCount,
            RestoredAvailableItemCount = summary.RestoredAvailableItemCount
        };
    }

    private static CourseContentSyncApplyResult CreateBlockedApplyResult(
        Guid courseId,
        CourseContentSyncPreviewResult preview,
        string message)
        => new()
        {
            CourseId = courseId,
            Status = CourseContentSyncApplyStatus.Blocked,
            Message = message,
            Preview = preview
        };

    private static CourseContentSyncApplyResult CreateFailedApplyResult(
        Guid courseId,
        CourseContentSyncPreviewResult preview,
        string message)
        => new()
        {
            CourseId = courseId,
            Status = CourseContentSyncApplyStatus.Failed,
            Message = message,
            Preview = preview
        };

    private static CourseContentSyncPreviewResult CreateError(
        Guid courseId,
        CourseContentSyncPreviewStatus status,
        string message,
        string courseTitle = "",
        string rootPath = "",
        DateTime? scannedAtUtc = null)
        => new()
        {
            CourseId = courseId,
            CourseTitle = courseTitle,
            RootPath = rootPath,
            ScannedAtUtc = scannedAtUtc,
            Status = status,
            Message = message
        };

    private static string ResolveCourseTitle(CourseRecord course)
        => string.IsNullOrWhiteSpace(course.Title)
            ? course.RawTitle
            : course.Title;

    private static async Task RollbackSafelyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
        }
    }

    private sealed record PreviewExecution(
        CourseRecord? PersistedCourse,
        DetectedCourseStructure? DetectedStructure,
        string ScannedRootPath,
        CourseContentSyncPreviewResult Preview)
    {
        public static PreviewExecution FromError(
            CourseContentSyncPreviewResult preview,
            CourseRecord? persistedCourse = null,
            string scannedRootPath = "",
            DetectedCourseStructure? detectedStructure = null)
            => new(persistedCourse, detectedStructure, scannedRootPath, preview);
    }
}
