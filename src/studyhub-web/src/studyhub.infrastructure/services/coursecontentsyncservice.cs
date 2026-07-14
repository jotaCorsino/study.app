using Microsoft.EntityFrameworkCore;
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
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ILocalCourseScanner _localCourseScanner = localCourseScanner;
    private readonly ILogger<CourseContentSyncService> _logger = logger;
    private readonly CourseContentSyncPlanner _planner = new();

    public async Task<CourseContentSyncPreviewResult> PreviewAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CourseRecord? course;
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            course = await context.Courses
                .AsNoTracking()
                .AsSplitQuery()
                .Include(record => record.Modules)
                    .ThenInclude(module => module.Topics)
                        .ThenInclude(topic => topic.Lessons)
                .SingleOrDefaultAsync(record => record.Id == courseId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load course {CourseId} for content sync preview.", courseId);
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Não foi possível carregar o curso para planejar a sincronização.");
        }

        if (course is null)
        {
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.CourseNotFound,
                "O curso não foi encontrado.");
        }

        var courseTitle = ResolveCourseTitle(course);
        if (course.SourceType != CourseSourceType.LocalFolder)
        {
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.UnsupportedCourseSource,
                "A origem desse curso não é uma pasta local.",
                courseTitle);
        }

        if (!LocalCourseSourceRootResolver.TryResolve(
                course.SourceMetadataJson,
                course.FolderPath,
                out var normalizedRootPath))
        {
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.SourceUnavailable,
                "A origem atual do curso não possui uma pasta local válida.",
                courseTitle);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceProbeError = ProbeSourceDirectory(
            courseId,
            courseTitle,
            normalizedRootPath);
        if (sourceProbeError is not null)
        {
            return sourceProbeError;
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
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.AccessDenied,
                "O acesso à pasta atual do curso foi negado.",
                courseTitle,
                normalizedRootPath);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            _logger.LogWarning(
                ex,
                "Course {CourseId} source {RootPath} became unavailable during sync preview.",
                courseId,
                normalizedRootPath);
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
                "I/O error while scanning course {CourseId} source {RootPath} for sync preview.",
                courseId,
                normalizedRootPath);
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.ScanFailed,
                "Não foi possível ler o conteúdo da pasta atual do curso.",
                courseTitle,
                normalizedRootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while scanning course {CourseId} source {RootPath} for sync preview.",
                courseId,
                normalizedRootPath);
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Ocorreu um erro inesperado ao examinar o conteúdo atual do curso.",
                courseTitle,
                normalizedRootPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (detectedStructure.LessonCount == 0)
        {
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.NoVideosFound,
                "A pasta atual do curso não contém vídeos reconhecidos.",
                courseTitle,
                normalizedRootPath,
                detectedStructure.ScannedAt);
        }

        try
        {
            return _planner.Plan(course, detectedStructure, normalizedRootPath);
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
            return CreateError(
                courseId,
                CourseContentSyncPreviewStatus.Unexpected,
                "Ocorreu um erro inesperado ao planejar a sincronização do curso.",
                courseTitle,
                normalizedRootPath,
                detectedStructure.ScannedAt);
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
}
