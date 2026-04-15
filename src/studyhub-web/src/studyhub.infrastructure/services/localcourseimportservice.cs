using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public class LocalCourseImportService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IFolderPickerService folderPickerService,
    ILocalFolderCourseBuilder localFolderCourseBuilder,
    ICourseEnrichmentOrchestrator courseEnrichmentOrchestrator,
    ILogger<LocalCourseImportService> logger) : ILocalCourseImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IFolderPickerService _folderPickerService = folderPickerService;
    private readonly ILocalFolderCourseBuilder _localFolderCourseBuilder = localFolderCourseBuilder;
    private readonly ICourseEnrichmentOrchestrator _courseEnrichmentOrchestrator = courseEnrichmentOrchestrator;
    private readonly ILogger<LocalCourseImportService> _logger = logger;

    public async Task<LocalCourseImportResult> PickAndImportAsync(CancellationToken cancellationToken = default)
    {
        string? selectedFolder;

        try
        {
            selectedFolder = await _folderPickerService.PickFolderAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local course folder picker failed to open.");
            return LocalCourseImportResult.Failed("Nao foi possivel abrir o seletor de pastas.", LocalCourseImportErrorKind.Unexpected);
        }

        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return LocalCourseImportResult.Cancelled();
        }

        return await ImportFromFolderAsync(selectedFolder, cancellationToken);
    }

    public async Task<LocalCourseImportResult> ImportFromFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        string normalizedFolderPath;
        try
        {
            normalizedFolderPath = Path.GetFullPath(folderPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid local course folder path received: {FolderPath}", folderPath);
            return LocalCourseImportResult.Failed("A pasta selecionada e invalida.", LocalCourseImportErrorKind.InvalidFolder);
        }

        if (!Directory.Exists(normalizedFolderPath))
        {
            return LocalCourseImportResult.Failed("A pasta selecionada nao foi encontrada ou nao esta acessivel.", LocalCourseImportErrorKind.InvalidFolder);
        }

        _logger.LogInformation("Local course import started. FolderPath: {FolderPath}", normalizedFolderPath);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var existingCourse = await context.Courses
                .Include(course => course.Modules)
                    .ThenInclude(module => module.Topics)
                        .ThenInclude(topic => topic.Lessons)
                .FirstOrDefaultAsync(course =>
                    course.SourceType == CourseSourceType.LocalFolder &&
                    course.FolderPath == normalizedFolderPath,
                    cancellationToken);
            var existingCourseDomain = existingCourse?.ToDomain();

            var existingLessonStates = existingCourse?
                .Modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .ToDictionary(
                    lesson => lesson.Id,
                    lesson => new LocalFolderLessonStateSnapshot
                    {
                        Status = lesson.Status,
                        WatchedPercentage = lesson.WatchedPercentage,
                        DurationMinutes = lesson.DurationMinutes,
                        LastPlaybackPositionSeconds = lesson.LastPlaybackPositionSeconds
                    })
                ?? [];

            LocalFolderCourseBuildResult buildResult;
            try
            {
                buildResult = await _localFolderCourseBuilder.BuildAsync(new LocalFolderCourseBuildRequest
                {
                    FolderPath = normalizedFolderPath,
                    ExistingAddedAt = existingCourse?.AddedAt,
                    ExistingLastAccessedAt = existingCourse?.LastAccessedAt,
                    ExistingLessonStates = existingLessonStates
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "Selected local course folder no longer exists: {FolderPath}", normalizedFolderPath);
                return LocalCourseImportResult.Failed("A pasta selecionada nao foi encontrada ou nao esta acessivel.", LocalCourseImportErrorKind.InvalidFolder);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied while scanning local course folder: {FolderPath}", normalizedFolderPath);
                return LocalCourseImportResult.Failed("Nao foi possivel acessar toda a estrutura da pasta selecionada.", LocalCourseImportErrorKind.ScanFailed);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "I/O error while scanning local course folder: {FolderPath}", normalizedFolderPath);
                return LocalCourseImportResult.Failed("Erro ao escanear a estrutura da pasta selecionada.", LocalCourseImportErrorKind.ScanFailed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while scanning local course folder: {FolderPath}", normalizedFolderPath);
                return LocalCourseImportResult.Failed("Ocorreu um erro inesperado ao escanear a pasta selecionada.", LocalCourseImportErrorKind.Unexpected);
            }

            var detectedStructure = buildResult.DetectedStructure;
            if (detectedStructure.LessonCount == 0)
            {
                return LocalCourseImportResult.Failed("Nenhum video compativel foi encontrado na pasta selecionada.", LocalCourseImportErrorKind.NoVideosFound);
            }

            var currentLessonId = existingCourse?.CurrentLessonId;
            var existingRoadmap = await context.CourseRoadmaps.FirstOrDefaultAsync(item => item.CourseId == detectedStructure.CourseId, cancellationToken);
            var existingMaterials = await context.CourseMaterials.FirstOrDefaultAsync(item => item.CourseId == detectedStructure.CourseId, cancellationToken);
            var importSnapshot = await context.CourseImportSnapshots.FirstOrDefaultAsync(item => item.CourseId == detectedStructure.CourseId, cancellationToken);

            var course = buildResult.Course;
            var structureChanged = existingCourseDomain == null || CoursePresentationMergeHelper.HasStructureChanged(course, existingCourseDomain);
            if (existingCourseDomain != null)
            {
                CoursePresentationMergeHelper.MergeExistingPresentation(course, existingCourseDomain);
            }

            var structureJson = JsonSerializer.Serialize(detectedStructure, JsonOptions);
            var courseRecord = course.ToRecord();

            if (currentLessonId.HasValue && ContainsLesson(courseRecord, currentLessonId.Value))
            {
                courseRecord.CurrentLessonId = currentLessonId;
            }

            if (existingCourse == null)
            {
                await context.Courses.AddAsync(courseRecord, cancellationToken);
            }
            else
            {
                var existingModules = existingCourse.Modules.ToList();
                if (existingModules.Count > 0)
                {
                    context.Modules.RemoveRange(existingModules);
                    await context.SaveChangesAsync(cancellationToken);
                }

                existingCourse.Modules.Clear();
                existingCourse.RawTitle = courseRecord.RawTitle;
                existingCourse.RawDescription = courseRecord.RawDescription;
                existingCourse.Title = courseRecord.Title;
                existingCourse.Description = courseRecord.Description;
                existingCourse.Category = courseRecord.Category;
                existingCourse.ThumbnailUrl = courseRecord.ThumbnailUrl;
                existingCourse.FolderPath = courseRecord.FolderPath;
                existingCourse.SourceType = courseRecord.SourceType;
                existingCourse.SourceMetadataJson = courseRecord.SourceMetadataJson;
                existingCourse.TotalDurationMinutes = courseRecord.TotalDurationMinutes;
                existingCourse.AddedAt = courseRecord.AddedAt;
                existingCourse.LastAccessedAt = courseRecord.LastAccessedAt;
                existingCourse.CurrentLessonId = courseRecord.CurrentLessonId;

                foreach (var module in courseRecord.Modules)
                {
                    existingCourse.Modules.Add(module);
                }
            }

            if (importSnapshot == null)
            {
                importSnapshot = new CourseImportSnapshotRecord
                {
                    CourseId = detectedStructure.CourseId
                };

                await context.CourseImportSnapshots.AddAsync(importSnapshot, cancellationToken);
            }

            importSnapshot.SourceKind = "local-folder";
            importSnapshot.RootFolderPath = detectedStructure.RootFolderPath;
            importSnapshot.StructureJson = structureJson;
            importSnapshot.ImportedAt = DateTime.UtcNow;

            if (existingRoadmap == null)
            {
                await context.CourseRoadmaps.AddAsync(new CourseRoadmapRecord
                {
                    CourseId = detectedStructure.CourseId,
                    LevelsJson = PersistenceMapper.SerializeRoadmap(new List<RoadmapLevel>()),
                    UpdatedAt = DateTime.Now
                }, cancellationToken);
            }

            if (existingMaterials == null)
            {
                await context.CourseMaterials.AddAsync(new CourseMaterialsRecord
                {
                    CourseId = detectedStructure.CourseId,
                    MaterialsJson = PersistenceMapper.SerializeMaterials(new List<Material>()),
                    UpdatedAt = DateTime.Now
                }, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TriggerPostImportEnrichment(detectedStructure.CourseId, structureChanged);

            _logger.LogInformation(
                "Local course import completed. CourseId: {CourseId}. Status: {Status}. FolderPath: {FolderPath}. Modules: {ModuleCount}. Topics: {TopicCount}. Lessons: {LessonCount}",
                detectedStructure.CourseId,
                existingCourse == null ? LocalCourseImportStatus.Imported : LocalCourseImportStatus.Updated,
                detectedStructure.RootFolderPath,
                detectedStructure.Modules.Count,
                detectedStructure.TopicCount,
                detectedStructure.LessonCount);

            return new LocalCourseImportResult
            {
                Status = existingCourse == null ? LocalCourseImportStatus.Imported : LocalCourseImportStatus.Updated,
                CourseId = detectedStructure.CourseId,
                CourseTitle = course.Title,
                FolderPath = detectedStructure.RootFolderPath,
                ModuleCount = detectedStructure.Modules.Count,
                TopicCount = detectedStructure.TopicCount,
                LessonCount = detectedStructure.LessonCount
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update failed while importing local course from folder: {FolderPath}", normalizedFolderPath);
            return LocalCourseImportResult.Failed("Erro ao persistir o curso importado.", LocalCourseImportErrorKind.PersistenceFailed);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Entity tracking failed while importing local course from folder: {FolderPath}", normalizedFolderPath);
            return LocalCourseImportResult.Failed("Erro ao atualizar os dados do curso importado.", LocalCourseImportErrorKind.PersistenceFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while importing local course from folder: {FolderPath}", normalizedFolderPath);
            return LocalCourseImportResult.Failed("Ocorreu um erro inesperado ao importar o curso.", LocalCourseImportErrorKind.Unexpected);
        }
    }

    private static bool ContainsLesson(CourseRecord courseRecord, Guid lessonId)
    {
        return courseRecord.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Any(lesson => lesson.Id == lessonId);
    }

    private void TriggerPostImportEnrichment(Guid courseId, bool structureChanged)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _courseEnrichmentOrchestrator.EnrichLocalCourseAsync(new studyhub.application.Contracts.Integrations.LocalCourseEnrichmentRequest
                {
                    CourseId = courseId,
                    StructureChanged = structureChanged
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background enrichment for local course {CourseId} failed after import.", courseId);
            }
        });
    }
}
