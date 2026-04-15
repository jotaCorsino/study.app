using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;
using studyhub.domain.AIContracts;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public sealed class CourseEnrichmentOrchestrator(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IIntegrationSettingsService integrationSettingsService,
    IGeminiCourseProvider geminiCourseProvider,
    CourseTextEnrichmentService courseTextEnrichmentService,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    IRoadmapService roadmapService,
    IMaterialService materialService,
    ILogger<CourseEnrichmentOrchestrator> logger) : ICourseEnrichmentOrchestrator
{
    private const string PresentationStepKey = "local-course-presentation";
    private const string TextRefinementStepKey = "local-course-text-refinement";
    private const string RoadmapStepKey = "roadmap-generation";
    private const string MaterialsStepKey = "supplementary-materials-generation";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> CourseLocks = new();
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly CourseTextEnrichmentService _courseTextEnrichmentService = courseTextEnrichmentService;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly IRoadmapService _roadmapService = roadmapService;
    private readonly IMaterialService _materialService = materialService;
    private readonly ILogger<CourseEnrichmentOrchestrator> _logger = logger;

    public async Task<LocalCourseEnrichmentResponse> EnrichLocalCourseAsync(LocalCourseEnrichmentRequest request, CancellationToken cancellationToken = default)
    {
        var gate = CourseLocks.GetOrAdd(request.CourseId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            return await EnrichLocalCourseInternalAsync(request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LocalCourseEnrichmentResponse> EnrichLocalCourseInternalAsync(LocalCourseEnrichmentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Local course enrichment started. CourseId: {CourseId}. StructureChanged: {StructureChanged}. ForceRefresh: {ForceRefresh}. Presentation: {RefreshPresentation}. Text: {RefreshTextRefinement}. Roadmap: {RefreshRoadmap}. Materials: {RefreshMaterials}",
            request.CourseId,
            request.StructureChanged,
            request.ForceRefresh,
            request.RefreshPresentation,
            request.RefreshTextRefinement,
            request.RefreshRoadmap,
            request.RefreshMaterials);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .FirstOrDefaultAsync(course => course.Id == request.CourseId, cancellationToken);

        if (record == null)
        {
            _logger.LogWarning("Local course enrichment skipped because the course was not found. CourseId: {CourseId}", request.CourseId);
            return new LocalCourseEnrichmentResponse
            {
                CourseId = request.CourseId,
                CompletedSteps = []
            };
        }

        var course = record.ToDomain();
        var completedSteps = course.SourceMetadata.CompletedSteps.ToList();
        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        var stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(course.Id, cancellationToken);
        var roadmapRecord = await context.CourseRoadmaps
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == course.Id, cancellationToken);
        var materialsRecord = await context.CourseMaterials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == course.Id, cancellationToken);
        var presentationUpdated = false;
        var textRefinementUpdated = false;
        var courseUpdated = false;

        if (request.RefreshPresentation && settings.HasGeminiKey && ShouldRunPresentation(request.StructureChanged, request.ForceRefresh, stepStates))
        {
            try
            {
                var presentationRequest = BuildPresentationRequest(course);
                var presentation = await ExecuteStepAsync(
                    course.Id,
                    PresentationStepKey,
                    "Gemini",
                    presentationRequest,
                    () => _geminiCourseProvider.GenerateCoursePresentationAsync(presentationRequest, cancellationToken),
                    cancellationToken);

                ApplyPresentation(course, presentation);
                presentationUpdated = true;
                courseUpdated = true;
                completedSteps = MergeSteps(completedSteps, ["LocalCoursePresentationGenerated"]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local course enrichment skipped presentation update for {CourseId}.", course.Id);
            }
        }
        else if (request.RefreshPresentation && !settings.HasGeminiKey && (!HasSucceeded(stepStates, PresentationStepKey) || request.StructureChanged))
        {
            await RecordSkippedStepAsync(course.Id, PresentationStepKey, "StudyHub", "Chave de Gemini ausente ou etapa desativada.", cancellationToken);
        }

        if (request.RefreshTextRefinement && settings.HasGeminiKey && ShouldRunTextRefinement(request.StructureChanged, request.ForceRefresh, stepStates))
        {
            try
            {
                var refinement = await _courseTextEnrichmentService.RefineCourseAsync(
                    course,
                    TextRefinementStepKey,
                    forceRefresh: request.StructureChanged || request.ForceRefresh,
                    cancellationToken);

                textRefinementUpdated = refinement.Updated;
                courseUpdated = courseUpdated || refinement.Updated || refinement.ProcessedBatches > 0;
                if (refinement.ProcessedBatches > 0 || refinement.SkippedBatches > 0)
                {
                    completedSteps = MergeSteps(completedSteps, ["LocalCourseTextRefined"]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local course enrichment skipped text refinement for {CourseId}.", course.Id);
            }
        }
        else if (request.RefreshTextRefinement && !settings.HasGeminiKey && (!HasSucceeded(stepStates, TextRefinementStepKey) || request.StructureChanged))
        {
            await RecordSkippedStepAsync(course.Id, TextRefinementStepKey, "StudyHub", "Chave de Gemini ausente ou etapa desativada.", cancellationToken);
        }

        if (courseUpdated)
        {
            course.SourceMetadata.LastEnrichedAt = DateTime.UtcNow;
            course.SourceMetadata.CompletedSteps = completedSteps;
            course.SourceMetadata.GenerationSummary = "Curso local enriquecido sobre a estrutura original importada do disco.";
            await CoursePersistenceHelper.UpsertCourseAsync(context, course, cancellationToken);
        }

        var roadmapUpdated = false;
        if (request.RefreshRoadmap && settings.HasGeminiKey && ShouldRunRoadmap(request.StructureChanged, request.ForceRefresh, stepStates, roadmapRecord))
        {
            await _roadmapService.GenerateRoadmapAsync(course.Id);
            stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(course.Id, cancellationToken);
            roadmapUpdated = HasSucceeded(stepStates, RoadmapStepKey);
            if (roadmapUpdated)
            {
                completedSteps = MergeSteps(completedSteps, ["RoadmapGenerated"]);
            }
        }
        else if (request.RefreshRoadmap && !settings.HasGeminiKey && (!HasSucceeded(stepStates, RoadmapStepKey) || request.StructureChanged))
        {
            await RecordSkippedStepAsync(course.Id, RoadmapStepKey, "StudyHub", "Chave de Gemini ausente ou etapa desativada.", cancellationToken);
        }

        var materialsUpdated = false;
        if (request.RefreshMaterials && settings.HasGeminiKey && settings.HasYouTubeKey && ShouldRunMaterials(request.StructureChanged, request.ForceRefresh, stepStates, materialsRecord))
        {
            await _materialService.RefreshMaterialsAsync(course.Id);
            stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(course.Id, cancellationToken);
            materialsUpdated = HasSucceeded(stepStates, MaterialsStepKey);
            if (materialsUpdated)
            {
                completedSteps = MergeSteps(completedSteps, ["SupplementaryMaterialsGenerated"]);
            }
        }
        else if (request.RefreshMaterials && (!settings.HasGeminiKey || !settings.HasYouTubeKey) && (!HasSucceeded(stepStates, MaterialsStepKey) || request.StructureChanged))
        {
            await RecordSkippedStepAsync(course.Id, MaterialsStepKey, "StudyHub", "Chaves de Gemini/YouTube ausentes ou etapa desativada.", cancellationToken);
        }

        var response = new LocalCourseEnrichmentResponse
        {
            CourseId = course.Id,
            PresentationUpdated = presentationUpdated,
            TextRefinementUpdated = textRefinementUpdated,
            RoadmapUpdated = roadmapUpdated,
            MaterialsUpdated = materialsUpdated,
            CompletedSteps = completedSteps
        };

        _logger.LogInformation(
            "Local course enrichment completed. CourseId: {CourseId}. PresentationUpdated: {PresentationUpdated}. TextRefinementUpdated: {TextRefinementUpdated}. RoadmapUpdated: {RoadmapUpdated}. MaterialsUpdated: {MaterialsUpdated}",
            response.CourseId,
            response.PresentationUpdated,
            response.TextRefinementUpdated,
            response.RoadmapUpdated,
            response.MaterialsUpdated);

        return response;
    }

    private static CoursePresentationRequestContract BuildPresentationRequest(Course course)
    {
        return new CoursePresentationRequestContract
        {
            Goal = "Enriquecer um curso local existente sem trocar sua estrutura primaria detectada do disco.",
            CourseContext = new CourseContextContract
            {
                SourceType = course.SourceType.ToString(),
                RawCourseTitle = FirstNonEmpty(course.RawTitle, course.Title),
                ModuleCount = course.Modules.Count,
                LessonCount = course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count(),
                DetectedModules = course.Modules
                    .OrderBy(module => module.Order)
                    .Select(module => new ModuleContextContract
                    {
                        RawTitle = FirstNonEmpty(module.RawTitle, module.Title),
                        LessonCount = module.Topics.SelectMany(topic => topic.Lessons).Count()
                    })
                    .ToList()
            }
        };
    }

    private static void ApplyPresentation(Course course, CoursePresentationResponseContract response)
    {
        course.Title = FirstNonEmpty(response.CourseTitle, course.Title);
        course.Description = FirstNonEmpty(response.CourseDescription, course.Description);

        foreach (var module in course.Modules)
        {
            var displayModule = response.DisplayModules.FirstOrDefault(item =>
                string.Equals(item.RawTitle, module.RawTitle, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(displayModule?.DisplayTitle))
            {
                module.Title = displayModule.DisplayTitle.Trim();
            }
        }
    }

    private static bool ShouldRunPresentation(bool structureChanged, bool forceRefresh, IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates)
    {
        return forceRefresh || structureChanged || !HasSucceeded(stepStates, PresentationStepKey);
    }

    private static bool ShouldRunTextRefinement(bool structureChanged, bool forceRefresh, IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates)
    {
        return forceRefresh || structureChanged || !HasSucceeded(stepStates, TextRefinementStepKey);
    }

    private static bool ShouldRunRoadmap(
        bool structureChanged,
        bool forceRefresh,
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        CourseRoadmapRecord? roadmapRecord)
    {
        return forceRefresh || structureChanged || !HasSucceeded(stepStates, RoadmapStepKey) || !HasMeaningfulRoadmap(roadmapRecord);
    }

    private static bool ShouldRunMaterials(
        bool structureChanged,
        bool forceRefresh,
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        CourseMaterialsRecord? materialsRecord)
    {
        return forceRefresh || structureChanged || !HasSucceeded(stepStates, MaterialsStepKey) || !HasMeaningfulMaterials(materialsRecord);
    }

    private static bool HasSucceeded(IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates, string stepKey)
    {
        return stepStates.TryGetValue(stepKey, out var step) && step.Status == CourseGenerationStepStatus.Succeeded;
    }

    private static bool HasMeaningfulRoadmap(CourseRoadmapRecord? roadmapRecord)
    {
        return roadmapRecord != null &&
            PersistenceMapper.DeserializeRoadmap(roadmapRecord.LevelsJson).Count > 0;
    }

    private static bool HasMeaningfulMaterials(CourseMaterialsRecord? materialsRecord)
    {
        return materialsRecord != null &&
            PersistenceMapper.DeserializeMaterials(materialsRecord.MaterialsJson).Count > 0;
    }

    private async Task<TResponse> ExecuteStepAsync<TRequest, TResponse>(
        Guid courseId,
        string stepKey,
        string provider,
        TRequest request,
        Func<Task<TResponse>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = stepKey,
                Provider = provider,
                Status = CourseGenerationStepStatus.Running,
                RequestJson = IntegrationJsonHelper.Serialize(request)
            }, cancellationToken);

            var response = await action();
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = stepKey,
                Provider = provider,
                Status = CourseGenerationStepStatus.Succeeded,
                RequestJson = IntegrationJsonHelper.Serialize(request),
                ResponseJson = IntegrationJsonHelper.Serialize(response)
            }, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = stepKey,
                Provider = provider,
                Status = CourseGenerationStepStatus.Failed,
                RequestJson = IntegrationJsonHelper.Serialize(request),
                ErrorMessage = ex.Message
            }, cancellationToken);
            throw;
        }
    }

    private Task RecordSkippedStepAsync(Guid courseId, string stepKey, string provider, string reason, CancellationToken cancellationToken)
    {
        return _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = stepKey,
            Provider = provider,
            Status = CourseGenerationStepStatus.Skipped,
            ErrorMessage = reason
        }, cancellationToken);
    }

    private static List<string> MergeSteps(IEnumerable<string> currentSteps, IEnumerable<string> newSteps)
    {
        return currentSteps
            .Concat(newSteps)
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
