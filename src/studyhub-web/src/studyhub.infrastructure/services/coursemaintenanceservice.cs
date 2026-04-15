using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Contracts.Maintenance;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public sealed class CourseMaintenanceService(
    ICourseService courseService,
    IOnlineCourseOperationsService onlineCourseOperationsService,
    ICourseEnrichmentOrchestrator courseEnrichmentOrchestrator,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    IMaterialService materialService,
    ILocalCourseImportService localCourseImportService,
    IDbContextFactory<StudyHubDbContext> contextFactory,
    ILogger<CourseMaintenanceService> logger) : ICourseMaintenanceService
{
    private readonly ICourseService _courseService = courseService;
    private readonly IOnlineCourseOperationsService _onlineCourseOperationsService = onlineCourseOperationsService;
    private readonly ICourseEnrichmentOrchestrator _courseEnrichmentOrchestrator = courseEnrichmentOrchestrator;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly IMaterialService _materialService = materialService;
    private readonly ILocalCourseImportService _localCourseImportService = localCourseImportService;
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ILogger<CourseMaintenanceService> _logger = logger;

    public async Task<CourseMaintenanceOperationResult> RegeneratePresentationAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return NotFound(courseId, "regenerate-presentation", "Curso nao encontrado para regenerar a apresentacao.");
        }

        _logger.LogInformation("Course maintenance started. Operation: regenerate-presentation. CourseId: {CourseId}. SourceType: {SourceType}", courseId, course.SourceType);

        if (course.SourceType == CourseSourceType.OnlineCurated)
        {
            var result = await _onlineCourseOperationsService.RetryStageAsync(courseId, OnlineCourseOperationalStage.Presentation, cancellationToken);
            return MapOnlineOperationResult("regenerate-presentation", result);
        }

        if (course.SourceType == CourseSourceType.ExternalImport)
        {
            return new CourseMaintenanceOperationResult
            {
                Success = false,
                CourseId = courseId,
                OperationKey = "regenerate-presentation",
                Message = "A regeneracao de apresentacao para ExternalImport ainda nao foi habilitada."
            };
        }

        await _courseEnrichmentOrchestrator.EnrichLocalCourseAsync(new LocalCourseEnrichmentRequest
        {
            CourseId = courseId,
            StructureChanged = false,
            ForceRefresh = true,
            RefreshPresentation = true,
            RefreshTextRefinement = false,
            RefreshRoadmap = false,
            RefreshMaterials = false
        }, cancellationToken);

        _logger.LogInformation("Course maintenance completed. Operation: regenerate-presentation. CourseId: {CourseId}", courseId);

        return new CourseMaintenanceOperationResult
        {
            Success = true,
            CourseId = courseId,
            OperationKey = "regenerate-presentation",
            Message = "Apresentacao do curso local regenerada com sucesso."
        };
    }

    public async Task<CourseMaintenanceOperationResult> RegenerateTextRefinementAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return NotFound(courseId, "regenerate-text-refinement", "Curso nao encontrado para regenerar o refinamento textual.");
        }

        _logger.LogInformation("Course maintenance started. Operation: regenerate-text-refinement. CourseId: {CourseId}. SourceType: {SourceType}", courseId, course.SourceType);

        if (course.SourceType == CourseSourceType.OnlineCurated)
        {
            var result = await _onlineCourseOperationsService.RetryStageAsync(courseId, OnlineCourseOperationalStage.TextRefinement, cancellationToken);
            return MapOnlineOperationResult("regenerate-text-refinement", result);
        }

        if (course.SourceType == CourseSourceType.ExternalImport)
        {
            return new CourseMaintenanceOperationResult
            {
                Success = false,
                CourseId = courseId,
                OperationKey = "regenerate-text-refinement",
                Message = "O refinamento textual para ExternalImport ainda nao foi habilitado."
            };
        }

        await _courseEnrichmentOrchestrator.EnrichLocalCourseAsync(new LocalCourseEnrichmentRequest
        {
            CourseId = courseId,
            StructureChanged = false,
            ForceRefresh = true,
            RefreshPresentation = false,
            RefreshTextRefinement = true,
            RefreshRoadmap = false,
            RefreshMaterials = false
        }, cancellationToken);

        _logger.LogInformation("Course maintenance completed. Operation: regenerate-text-refinement. CourseId: {CourseId}", courseId);

        return new CourseMaintenanceOperationResult
        {
            Success = true,
            CourseId = courseId,
            OperationKey = "regenerate-text-refinement",
            Message = "Refinamento textual do curso local regenerado com sucesso."
        };
    }

    public async Task<CourseMaintenanceOperationResult> RegenerateSupplementaryMaterialsAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return NotFound(courseId, "regenerate-supplementary-materials", "Curso nao encontrado para regenerar materiais complementares.");
        }

        _logger.LogInformation("Course maintenance started. Operation: regenerate-supplementary-materials. CourseId: {CourseId}", courseId);

        await _materialService.RefreshMaterialsAsync(courseId);
        var stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(courseId, cancellationToken);
        var step = stepStates.TryGetValue(OnlineCourseStepKeys.SupplementaryMaterialsGeneration, out var entry) ? entry : null;
        var success = step?.Status == CourseGenerationStepStatus.Succeeded;

        _logger.LogInformation(
            "Course maintenance completed. Operation: regenerate-supplementary-materials. CourseId: {CourseId}. Success: {Success}",
            courseId,
            success);

        return new CourseMaintenanceOperationResult
        {
            Success = success,
            CourseId = courseId,
            OperationKey = "regenerate-supplementary-materials",
            Message = success
                ? "Materiais complementares regenerados com sucesso."
                : FirstNonEmpty(step?.LastErrorMessage, step?.ErrorMessage, "A regeneracao dos materiais complementares terminou sem sucesso."),
            AffectedItems = success ? 1 : 0
        };
    }

    public async Task<CourseMaintenanceOperationResult> RevalidateOnlineCourseAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return NotFound(courseId, "revalidate-online-course", "Curso nao encontrado para revalidacao.");
        }

        if (course.SourceType != CourseSourceType.OnlineCurated)
        {
            return new CourseMaintenanceOperationResult
            {
                Success = false,
                CourseId = courseId,
                OperationKey = "revalidate-online-course",
                Message = "A revalidacao online so se aplica a cursos OnlineCurated."
            };
        }

        _logger.LogInformation("Course maintenance started. Operation: revalidate-online-course. CourseId: {CourseId}", courseId);
        var result = await _onlineCourseOperationsService.RetryStageAsync(courseId, OnlineCourseOperationalStage.Validation, cancellationToken);
        return MapOnlineOperationResult("revalidate-online-course", result);
    }

    public async Task<CourseMaintenanceOperationResult> ResyncLocalCourseAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return NotFound(courseId, "resync-local-course", "Curso nao encontrado para ressincronizacao.");
        }

        if (course.SourceType != CourseSourceType.LocalFolder || string.IsNullOrWhiteSpace(course.FolderPath))
        {
            return new CourseMaintenanceOperationResult
            {
                Success = false,
                CourseId = courseId,
                OperationKey = "resync-local-course",
                Message = "A ressincronizacao local so se aplica a cursos LocalFolder com pasta persistida."
            };
        }

        _logger.LogInformation(
            "Course maintenance started. Operation: resync-local-course. CourseId: {CourseId}. FolderPath: {FolderPath}",
            courseId,
            course.FolderPath);

        var result = await _localCourseImportService.ImportFromFolderAsync(course.FolderPath, cancellationToken);
        var success = result.Status is LocalCourseImportStatus.Imported or LocalCourseImportStatus.Updated;

        _logger.LogInformation(
            "Course maintenance completed. Operation: resync-local-course. CourseId: {CourseId}. Success: {Success}. Status: {Status}",
            courseId,
            success,
            result.Status);

        return new CourseMaintenanceOperationResult
        {
            Success = success,
            CourseId = courseId,
            OperationKey = "resync-local-course",
            Message = success
                ? "Curso local ressincronizado com sucesso."
                : FirstNonEmpty(result.Message, "A ressincronizacao do curso local nao foi concluida com sucesso."),
            AffectedItems = success ? result.LessonCount : 0
        };
    }

    public async Task<CourseMaintenanceOperationResult> ClearBrokenOperationalStateAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Course maintenance started. Operation: clear-broken-operational-state. CourseId: {CourseId}", courseId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var courseRecord = await context.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);

        if (courseRecord == null)
        {
            return NotFound(courseId, "clear-broken-operational-state", "Curso nao encontrado para limpeza de estado operacional.");
        }

        var lessonIds = courseRecord.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Select(lesson => lesson.Id)
            .ToHashSet();

        var brokenSteps = await context.CourseGenerationSteps
            .Where(item =>
                item.CourseId == courseId &&
                (string.Equals(item.Status, CourseGenerationStepStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(item.Status, CourseGenerationStepStatus.Running.ToString(), StringComparison.OrdinalIgnoreCase)))
            .ToListAsync(cancellationToken);

        var brokenExternalStates = await context.ExternalLessonRuntimeStates
            .Where(item =>
                item.CourseId == courseId &&
                (!lessonIds.Contains(item.LessonId) ||
                 !string.Equals(item.Status, "Ready", StringComparison.OrdinalIgnoreCase)))
            .ToListAsync(cancellationToken);

        if (courseRecord.CurrentLessonId is Guid currentLessonId && !lessonIds.Contains(currentLessonId))
        {
            courseRecord.CurrentLessonId = null;
        }

        if (brokenSteps.Count > 0)
        {
            context.CourseGenerationSteps.RemoveRange(brokenSteps);
        }

        if (brokenExternalStates.Count > 0)
        {
            context.ExternalLessonRuntimeStates.RemoveRange(brokenExternalStates);
        }

        await context.SaveChangesAsync(cancellationToken);

        var affectedItems = brokenSteps.Count + brokenExternalStates.Count;
        _logger.LogInformation(
            "Course maintenance completed. Operation: clear-broken-operational-state. CourseId: {CourseId}. ClearedSteps: {ClearedSteps}. ClearedExternalStates: {ClearedExternalStates}",
            courseId,
            brokenSteps.Count,
            brokenExternalStates.Count);

        return new CourseMaintenanceOperationResult
        {
            Success = true,
            CourseId = courseId,
            OperationKey = "clear-broken-operational-state",
            Message = affectedItems == 0
                ? "Nenhum estado operacional quebrado foi encontrado para este curso."
                : "Estados operacionais quebrados limpos com sucesso, preservando artefatos validos.",
            AffectedItems = affectedItems
        };
    }

    private static CourseMaintenanceOperationResult MapOnlineOperationResult(string operationKey, OnlineCourseStageExecutionResult result)
    {
        return new CourseMaintenanceOperationResult
        {
            Success = result.Success,
            CourseId = result.CourseId,
            OperationKey = operationKey,
            Message = result.Message,
            AffectedItems = result.Success ? 1 : 0
        };
    }

    private static CourseMaintenanceOperationResult NotFound(Guid courseId, string operationKey, string message)
    {
        return new CourseMaintenanceOperationResult
        {
            Success = false,
            CourseId = courseId,
            OperationKey = operationKey,
            Message = message
        };
    }

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
}
