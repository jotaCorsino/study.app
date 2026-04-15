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

public sealed partial class OnlineCourseOperationsService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IIntegrationSettingsService integrationSettingsService,
    IGeminiCourseProvider geminiCourseProvider,
    CourseTextEnrichmentService courseTextEnrichmentService,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    IExternalLessonRuntimeStateService externalLessonRuntimeStateService,
    IProgressService progressService,
    IRoadmapService roadmapService,
    IMaterialService materialService,
    OnlineCourseCreationOrchestrator onlineCourseCreationOrchestrator,
    ILogger<OnlineCourseOperationsService> logger) : IOnlineCourseOperationsService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly CourseTextEnrichmentService _courseTextEnrichmentService = courseTextEnrichmentService;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly IExternalLessonRuntimeStateService _externalLessonRuntimeStateService = externalLessonRuntimeStateService;
    private readonly IProgressService _progressService = progressService;
    private readonly IRoadmapService _roadmapService = roadmapService;
    private readonly IMaterialService _materialService = materialService;
    private readonly OnlineCourseCreationOrchestrator _onlineCourseCreationOrchestrator = onlineCourseCreationOrchestrator;
    private readonly ILogger<OnlineCourseOperationsService> _logger = logger;

    public async Task<OnlineCourseRuntimeState?> GetRuntimeStateAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var courseRecord = await LoadCourseRecordAsync(context, courseId, cancellationToken);
        var stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(courseId, cancellationToken);
        var externalStates = await _externalLessonRuntimeStateService.GetCourseStatesAsync(courseId, cancellationToken);
        var progress = await _progressService.GetProgressByCourseAsync(courseId);
        var roadmapRecord = await context.CourseRoadmaps
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == courseId, cancellationToken);
        var materialsRecord = await context.CourseMaterials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == courseId, cancellationToken);

        if (courseRecord == null || courseRecord.SourceType != CourseSourceType.OnlineCurated)
        {
            return new OnlineCourseRuntimeState
            {
                CourseId = courseId,
                Stages = BuildDefaultStageStates()
            };
        }

        var course = courseRecord.ToDomain();
        var structureFreshAt = GetLatestTimestamp(
            GetStepState(stepStates, OnlineCourseStepKeys.CoursePersisted)?.LastSucceededAt,
            GetStepState(stepStates, OnlineCourseStepKeys.OnlineCourseAssembly)?.LastSucceededAt,
            GetStepState(stepStates, OnlineCourseStepKeys.SourceCuration)?.LastSucceededAt,
            GetStepState(stepStates, OnlineCourseStepKeys.YouTubeDiscovery)?.LastSucceededAt,
            GetStepState(stepStates, OnlineCourseStepKeys.OnlinePlanning)?.LastSucceededAt);

        var roadmapLevels = roadmapRecord == null
            ? []
            : PersistenceMapper.DeserializeRoadmap(roadmapRecord.LevelsJson);
        var materials = materialsRecord == null
            ? []
            : PersistenceMapper.DeserializeMaterials(materialsRecord.MaterialsJson);

        return new OnlineCourseRuntimeState
        {
            CourseId = courseId,
            CurrentLessonId = courseRecord.CurrentLessonId ?? progress?.LastLessonId,
            OverallProgressPercentage = progress?.OverallPercentage ?? 0,
            Stages =
            [
                BuildStructureStage(course, stepStates),
                BuildPresentationStage(course, stepStates, structureFreshAt),
                BuildTextRefinementStage(course, stepStates, structureFreshAt),
                BuildRoadmapStage(stepStates, roadmapLevels.Count > 0, structureFreshAt),
                BuildSupplementaryMaterialsStage(stepStates, materials.Count > 0, structureFreshAt),
                BuildSourceCurationStage(stepStates, structureFreshAt),
                BuildValidationStage(courseRecord, stepStates, structureFreshAt),
                BuildExternalPlaybackStage(stepStates, externalStates)
            ]
        };
    }

    public async Task<OnlineCourseStageExecutionResult> RetryStageAsync(
        Guid courseId,
        OnlineCourseOperationalStage stage,
        CancellationToken cancellationToken = default)
    {
        var course = await LoadOnlineCourseAsync(courseId, cancellationToken);
        if (course == null)
        {
            return new OnlineCourseStageExecutionResult
            {
                Success = false,
                CourseId = courseId,
                Stage = stage,
                Message = "Curso online nao encontrado para retry desta etapa."
            };
        }

        return stage switch
        {
            OnlineCourseOperationalStage.Structure or OnlineCourseOperationalStage.SourceCuration => await RetrySourceCurationAsync(course, stage, cancellationToken),
            OnlineCourseOperationalStage.Presentation => await RetryPresentationAsync(course, cancellationToken),
            OnlineCourseOperationalStage.TextRefinement => await RetryTextRefinementAsync(course, cancellationToken),
            OnlineCourseOperationalStage.Roadmap => await RetryRoadmapAsync(courseId, cancellationToken),
            OnlineCourseOperationalStage.SupplementaryMaterials => await RetrySupplementaryMaterialsAsync(courseId, cancellationToken),
            OnlineCourseOperationalStage.Validation => await RetryValidationAsync(courseId, cancellationToken),
            OnlineCourseOperationalStage.ExternalPlayback => new OnlineCourseStageExecutionResult
            {
                Success = false,
                CourseId = courseId,
                Stage = stage,
                Message = "O retry de playback externo e controlado pela reabertura da aula no runtime do player."
            },
            _ => new OnlineCourseStageExecutionResult
            {
                Success = false,
                CourseId = courseId,
                Stage = stage,
                Message = "Etapa operacional nao suportada por esta operacao."
            }
        };
    }

    private async Task<OnlineCourseStageExecutionResult> RetrySourceCurationAsync(
        Course course,
        OnlineCourseOperationalStage stage,
        CancellationToken cancellationToken)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        if (!settings.HasGeminiKey || !settings.HasYouTubeKey)
        {
            return new OnlineCourseStageExecutionResult
            {
                Success = false,
                CourseId = course.Id,
                Stage = stage,
                Message = "Configure Gemini e YouTube antes de reexecutar a curadoria online."
            };
        }

        var intent = BuildIntentFromCourse(course);
        var result = await _onlineCourseCreationOrchestrator.RefreshCourseStructureAsync(
            intent,
            refreshArtifacts: false,
            cancellationToken);

        return new OnlineCourseStageExecutionResult
        {
            Success = result.Success,
            CourseId = course.Id,
            Stage = stage,
            Message = result.Message
        };
    }

    private async Task<OnlineCourseStageExecutionResult> RetryPresentationAsync(Course course, CancellationToken cancellationToken)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        if (!settings.HasGeminiKey)
        {
            return BuildFailureResult(course.Id, OnlineCourseOperationalStage.Presentation, "Configure a chave do Gemini antes de reexecutar a apresentacao do curso.");
        }

        var request = BuildPresentationRequest(course);
        await RecordRunningStepAsync(course.Id, OnlineCourseStepKeys.CoursePresentation, "Gemini", request, cancellationToken);

        try
        {
            var response = await _geminiCourseProvider.GenerateCoursePresentationAsync(request, cancellationToken);
            ApplyPresentation(course, response);
            TouchCourseMetadata(course, "OnlineCoursePresentationGenerated");
            await SaveCourseAsync(course, cancellationToken);

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = course.Id,
                StepKey = OnlineCourseStepKeys.CoursePresentation,
                Provider = "Gemini",
                Status = CourseGenerationStepStatus.Succeeded,
                RequestJson = IntegrationJsonHelper.Serialize(request),
                ResponseJson = IntegrationJsonHelper.Serialize(response)
            }, cancellationToken);

            return new OnlineCourseStageExecutionResult
            {
                Success = true,
                CourseId = course.Id,
                Stage = OnlineCourseOperationalStage.Presentation,
                Message = "Apresentacao do curso online reexecutada com sucesso."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online course presentation retry failed for course {CourseId}.", course.Id);
            await RecordFailedStepAsync(course.Id, OnlineCourseStepKeys.CoursePresentation, "Gemini", request, ex.Message, cancellationToken);
            return BuildFailureResult(course.Id, OnlineCourseOperationalStage.Presentation, ex.Message);
        }
    }

    private async Task<OnlineCourseStageExecutionResult> RetryTextRefinementAsync(Course course, CancellationToken cancellationToken)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        if (!settings.HasGeminiKey)
        {
            return BuildFailureResult(course.Id, OnlineCourseOperationalStage.TextRefinement, "Configure a chave do Gemini antes de reexecutar o refinamento textual.");
        }

        try
        {
            var result = await _courseTextEnrichmentService.RefineCourseAsync(
                course,
                OnlineCourseStepKeys.TextRefinement,
                forceRefresh: true,
                cancellationToken);

            TouchCourseMetadata(course, "OnlineCourseTextRefined");
            await SaveCourseAsync(course, cancellationToken);

            return new OnlineCourseStageExecutionResult
            {
                Success = result.FailedBatches == 0,
                CourseId = course.Id,
                Stage = OnlineCourseOperationalStage.TextRefinement,
                Message = result.FailedBatches == 0
                    ? "Refinamento textual do curso online reexecutado com sucesso."
                    : $"Refinamento textual reexecutado com falha parcial em {result.FailedBatches} lote(s)."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online course text refinement retry failed for course {CourseId}.", course.Id);
            await RecordFailedStepAsync(course.Id, OnlineCourseStepKeys.TextRefinement, "Gemini", new { course.Id }, ex.Message, cancellationToken);
            return BuildFailureResult(course.Id, OnlineCourseOperationalStage.TextRefinement, ex.Message);
        }
    }

    private async Task<OnlineCourseStageExecutionResult> RetryRoadmapAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await _roadmapService.GenerateRoadmapAsync(courseId);
        var step = await GetStepStateAsync(courseId, OnlineCourseStepKeys.RoadmapGeneration, cancellationToken);

        return step?.Status == CourseGenerationStepStatus.Succeeded
            ? new OnlineCourseStageExecutionResult
            {
                Success = true,
                CourseId = courseId,
                Stage = OnlineCourseOperationalStage.Roadmap,
                Message = "Roadmap do curso online reexecutado com sucesso."
            }
            : BuildFailureResult(courseId, OnlineCourseOperationalStage.Roadmap, FirstNonEmpty(step?.LastErrorMessage, step?.ErrorMessage, "Falha ao reexecutar o roadmap do curso online."));
    }

    private async Task<OnlineCourseStageExecutionResult> RetrySupplementaryMaterialsAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await _materialService.RefreshMaterialsAsync(courseId);
        var step = await GetStepStateAsync(courseId, OnlineCourseStepKeys.SupplementaryMaterialsGeneration, cancellationToken);

        return step?.Status == CourseGenerationStepStatus.Succeeded
            ? new OnlineCourseStageExecutionResult
            {
                Success = true,
                CourseId = courseId,
                Stage = OnlineCourseOperationalStage.SupplementaryMaterials,
                Message = "Materiais complementares do curso online reexecutados com sucesso."
            }
            : BuildFailureResult(courseId, OnlineCourseOperationalStage.SupplementaryMaterials, FirstNonEmpty(step?.LastErrorMessage, step?.ErrorMessage, "Falha ao reexecutar os materiais complementares."));
    }

    private async Task<OnlineCourseStageExecutionResult> RetryValidationAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await RecordRunningStepAsync(
            courseId,
            OnlineCourseStepKeys.OnlineCourseValidation,
            "StudyHub",
            new { courseId },
            cancellationToken);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var courseRecord = await LoadCourseRecordAsync(context, courseId, cancellationToken);
            if (courseRecord == null || courseRecord.SourceType != CourseSourceType.OnlineCurated)
            {
                return BuildFailureResult(courseId, OnlineCourseOperationalStage.Validation, "Curso online nao encontrado para validacao.");
            }

            var repairsApplied = 0;
            var externalLessonCount = 0;
            var invalidLessonCount = 0;
            var lessonIds = new HashSet<Guid>();

            foreach (var lesson in courseRecord.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons))
            {
                lessonIds.Add(lesson.Id);

                if (!string.IsNullOrWhiteSpace(lesson.ExternalUrl))
                {
                    externalLessonCount++;

                    if (lesson.SourceType != LessonSourceType.ExternalVideo)
                    {
                        lesson.SourceType = LessonSourceType.ExternalVideo;
                        repairsApplied++;
                    }

                    if (string.IsNullOrWhiteSpace(lesson.Provider) && IsYouTubeUrl(lesson.ExternalUrl))
                    {
                        lesson.Provider = "YouTube";
                        repairsApplied++;
                    }

                    if (!IsYouTubeUrl(lesson.ExternalUrl))
                    {
                        invalidLessonCount++;
                    }
                }
                else
                {
                    invalidLessonCount++;
                }
            }

            if (courseRecord.CurrentLessonId is Guid currentLessonId && !lessonIds.Contains(currentLessonId))
            {
                courseRecord.CurrentLessonId = ResolveCurrentLessonId(courseRecord);
                repairsApplied++;
            }

            await CoursePersistenceHelper.SaveAncillaryRecordsAsync(context, courseId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var responsePayload = new
            {
                courseId,
                repairsApplied,
                externalLessonCount,
                invalidLessonCount,
                currentLessonId = courseRecord.CurrentLessonId
            };

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.OnlineCourseValidation,
                Provider = "StudyHub",
                Status = invalidLessonCount == 0 ? CourseGenerationStepStatus.Succeeded : CourseGenerationStepStatus.Failed,
                RequestJson = IntegrationJsonHelper.Serialize(new { courseId }),
                ResponseJson = IntegrationJsonHelper.Serialize(responsePayload),
                ErrorMessage = invalidLessonCount == 0
                    ? string.Empty
                    : $"A validacao encontrou {invalidLessonCount} aula(s) online com fonte externa invalida ou incompleta."
            }, cancellationToken);

            return invalidLessonCount == 0
                ? new OnlineCourseStageExecutionResult
                {
                    Success = true,
                    CourseId = courseId,
                    Stage = OnlineCourseOperationalStage.Validation,
                    Message = "Validacao estrutural do curso online concluida com sucesso."
                }
                : BuildFailureResult(courseId, OnlineCourseOperationalStage.Validation, $"A validacao encontrou {invalidLessonCount} aula(s) online com fonte externa invalida ou incompleta.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online course validation retry failed for course {CourseId}.", courseId);
            await RecordFailedStepAsync(courseId, OnlineCourseStepKeys.OnlineCourseValidation, "StudyHub", new { courseId }, ex.Message, cancellationToken);
            return BuildFailureResult(courseId, OnlineCourseOperationalStage.Validation, ex.Message);
        }
    }

    private async Task<Course?> LoadOnlineCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var courseRecord = await LoadCourseRecordAsync(context, courseId, cancellationToken);
        return courseRecord?.SourceType == CourseSourceType.OnlineCurated
            ? courseRecord.ToDomain()
            : null;
    }

    private static Task<CourseRecord?> LoadCourseRecordAsync(StudyHubDbContext context, Guid courseId, CancellationToken cancellationToken)
    {
        return context.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);
    }

    private static OnlineCourseStageState BuildStructureStage(
        Course course,
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates)
    {
        var steps = GetOrderedStepStates(
            stepStates,
            OnlineCourseStepKeys.OnlinePlanning,
            OnlineCourseStepKeys.YouTubeDiscovery,
            OnlineCourseStepKeys.SourceCuration,
            OnlineCourseStepKeys.OnlineCourseAssembly,
            OnlineCourseStepKeys.CoursePersisted);

        var stage = BuildAggregateStage(
            OnlineCourseOperationalStage.Structure,
            "StudyHub",
            steps,
            "Estrutura do curso online gerada a partir de planejamento, descoberta, curadoria e persistencia.");

        var hasStructure = course.Modules.Count > 0 &&
                           course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Any();

        return ApplyArtifactStateHeuristic(
            stage,
            hasStructure,
            "O curso online possui estrutura persistida, mas sem trilha operacional completa desta etapa.");
    }

    private static OnlineCourseStageState BuildPresentationStage(
        Course course,
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        DateTime? structureFreshAt)
    {
        var step = GetStepState(stepStates, OnlineCourseStepKeys.CoursePresentation);
        var stage = BuildSingleStepStage(
            OnlineCourseOperationalStage.Presentation,
            "Gemini",
            step,
            "Apresentacao amigavel do curso online.");

        var hasPresentationArtifact = !string.IsNullOrWhiteSpace(course.Title) &&
                                     (!string.Equals(course.Title, course.RawTitle, StringComparison.OrdinalIgnoreCase) ||
                                      !string.IsNullOrWhiteSpace(course.Description));

        stage = ApplyArtifactStateHeuristic(
            stage,
            hasPresentationArtifact,
            "O curso online ja possui apresentacao persistida, mas sem rastreio completo desta etapa.");

        return ApplyStaleStageHeuristic(
            stage,
            structureFreshAt,
            "A apresentacao do curso online ficou desatualizada depois da ultima mudanca estrutural.");
    }

    private static OnlineCourseStageState BuildTextRefinementStage(
        Course course,
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        DateTime? structureFreshAt)
    {
        var step = GetStepState(stepStates, OnlineCourseStepKeys.TextRefinement);
        var stage = BuildSingleStepStage(
            OnlineCourseOperationalStage.TextRefinement,
            "Gemini",
            step,
            "Refinamento textual de modulos, aulas e descricoes curtas.");

        var hasRefinementArtifact = course.Modules.Any(module =>
            !string.Equals(module.Title, module.RawTitle, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(module.Description) ||
            module.Topics.SelectMany(topic => topic.Lessons).Any(lesson =>
                !string.Equals(lesson.Title, lesson.RawTitle, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(lesson.Description)));

        stage = ApplyArtifactStateHeuristic(
            stage,
            hasRefinementArtifact,
            "O curso online possui refinamentos textuais persistidos, mas sem historico completo desta etapa.");

        return ApplyStaleStageHeuristic(
            stage,
            structureFreshAt,
            "O refinamento textual do curso online ficou desatualizado depois da ultima mudanca estrutural.");
    }

    private static OnlineCourseStageState BuildRoadmapStage(
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        bool hasRoadmap,
        DateTime? structureFreshAt)
    {
        var step = GetStepState(stepStates, OnlineCourseStepKeys.RoadmapGeneration);
        var stage = BuildSingleStepStage(
            OnlineCourseOperationalStage.Roadmap,
            "Gemini",
            step,
            "Roadmap do curso online.");

        stage = ApplyArtifactStateHeuristic(
            stage,
            hasRoadmap,
            "Existe roadmap persistido para o curso online, mas sem historico operacional completo.");

        return ApplyStaleStageHeuristic(
            stage,
            structureFreshAt,
            "O roadmap ficou desatualizado depois da ultima mudanca estrutural do curso online.");
    }

    private static OnlineCourseStageState BuildSupplementaryMaterialsStage(
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        bool hasMaterials,
        DateTime? structureFreshAt)
    {
        var steps = GetOrderedStepStates(
            stepStates,
            OnlineCourseStepKeys.SupplementaryQueryGeneration,
            OnlineCourseStepKeys.SupplementaryYouTubeDiscovery,
            OnlineCourseStepKeys.SupplementaryMaterialsGeneration);

        var stage = BuildAggregateStage(
            OnlineCourseOperationalStage.SupplementaryMaterials,
            "Gemini + YouTube",
            steps,
            "Materiais complementares do curso online.");

        stage = ApplyArtifactStateHeuristic(
            stage,
            hasMaterials,
            "Existem materiais complementares persistidos para o curso online, mas sem historico operacional completo.");

        return ApplyStaleStageHeuristic(
            stage,
            structureFreshAt,
            "Os materiais complementares ficaram desatualizados depois da ultima mudanca estrutural do curso online.");
    }

    private static OnlineCourseStageState BuildSourceCurationStage(
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        DateTime? structureFreshAt)
    {
        var steps = GetOrderedStepStates(
            stepStates,
            OnlineCourseStepKeys.YouTubeDiscovery,
            OnlineCourseStepKeys.SourceCuration);

        var stage = BuildAggregateStage(
            OnlineCourseOperationalStage.SourceCuration,
            "YouTube + StudyHub",
            steps,
            "Curadoria de fontes do curso online.");

        if (stage.Status == OnlineCourseOperationalStatus.NotStarted && structureFreshAt.HasValue)
        {
            stage.Status = OnlineCourseOperationalStatus.Partial;
            stage.Summary = "A curadoria de fontes ja produziu estrutura persistida, mas sem historico operacional completo.";
        }

        return ApplyStaleStageHeuristic(
            stage,
            structureFreshAt,
            "A curadoria de fontes do curso online precisa ser revisada depois da ultima mudanca estrutural.",
            skipWhenStageSucceeded: false);
    }

    private static OnlineCourseStageState BuildValidationStage(
        CourseRecord courseRecord,
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        DateTime? structureFreshAt)
    {
        var step = GetStepState(stepStates, OnlineCourseStepKeys.OnlineCourseValidation);
        var stage = BuildSingleStepStage(
            OnlineCourseOperationalStage.Validation,
            "StudyHub",
            step,
            "Validacao operacional do curso online.");

        var hasInvalidCurrentLesson = courseRecord.CurrentLessonId is Guid currentLessonId &&
                                      !courseRecord.Modules.SelectMany(module => module.Topics)
                                          .SelectMany(topic => topic.Lessons)
                                          .Any(lesson => lesson.Id == currentLessonId);

        if (hasInvalidCurrentLesson && stage.Status == OnlineCourseOperationalStatus.Succeeded)
        {
            stage.Status = OnlineCourseOperationalStatus.Partial;
            stage.Summary = "A validacao precisa ser reexecutada porque a ultima aula aberta nao bate com a estrutura atual.";
        }

        return ApplyStaleStageHeuristic(
            stage,
            structureFreshAt,
            "A validacao do curso online ficou desatualizada depois da ultima mudanca estrutural.");
    }

    private static OnlineCourseStageState BuildExternalPlaybackStage(
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        IReadOnlyList<ExternalLessonRuntimeState> externalStates)
    {
        var step = GetStepState(stepStates, OnlineCourseStepKeys.ExternalPlayback);
        var failedStates = externalStates
            .Where(state => string.Equals(state.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(state => state.LastFailedAt ?? state.UpdatedAt)
            .ToList();
        var readyStates = externalStates
            .Where(state => string.Equals(state.Status, "Ready", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(state.Status, "Opened", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var status = step == null
            ? OnlineCourseOperationalStatus.NotStarted
            : MapStatus(step.Status);

        if (readyStates.Count > 0 && failedStates.Count > 0)
        {
            status = OnlineCourseOperationalStatus.Partial;
        }
        else if (readyStates.Count > 0)
        {
            status = OnlineCourseOperationalStatus.Succeeded;
        }
        else if (failedStates.Count > 0)
        {
            status = OnlineCourseOperationalStatus.Failed;
        }

        return new OnlineCourseStageState
        {
            Stage = OnlineCourseOperationalStage.ExternalPlayback,
            Provider = "YouTube",
            Status = status,
            Summary = externalStates.Count == 0
                ? "Nenhuma aula externa foi aberta neste curso online ainda."
                : $"Runtime externo com {readyStates.Count} aula(s) prontas e {failedStates.Count} falha(s) controladas.",
            LastErrorMessage = FirstNonEmpty(
                failedStates.FirstOrDefault()?.LastErrorMessage,
                step?.LastErrorMessage,
                step?.ErrorMessage),
            LastAttemptAt = GetLatestTimestamp(
                step?.CreatedAt,
                externalStates.Select(state => (DateTime?)state.UpdatedAt).ToArray()),
            LastSucceededAt = GetLatestTimestamp(
                step?.LastSucceededAt,
                externalStates.Select(state => state.LastSucceededAt).ToArray()),
            LastFailedAt = GetLatestTimestamp(
                step?.LastFailedAt,
                failedStates.Select(state => state.LastFailedAt).ToArray())
        };
    }

    private static OnlineCourseStageState BuildSingleStepStage(
        OnlineCourseOperationalStage stage,
        string provider,
        CourseGenerationStepEntry? step,
        string summary)
    {
        return new OnlineCourseStageState
        {
            Stage = stage,
            Provider = FirstNonEmpty(step?.Provider, provider),
            Status = step == null ? OnlineCourseOperationalStatus.NotStarted : MapStatus(step.Status),
            Summary = step == null ? summary : BuildSummaryFromStep(step, summary),
            LastErrorMessage = FirstNonEmpty(step?.LastErrorMessage, step?.ErrorMessage),
            LastAttemptAt = step?.CreatedAt,
            LastSucceededAt = step?.LastSucceededAt,
            LastFailedAt = step?.LastFailedAt
        };
    }

    private static OnlineCourseStageState BuildAggregateStage(
        OnlineCourseOperationalStage stage,
        string provider,
        IReadOnlyList<CourseGenerationStepEntry> steps,
        string summary)
    {
        if (steps.Count == 0)
        {
            return new OnlineCourseStageState
            {
                Stage = stage,
                Provider = provider,
                Status = OnlineCourseOperationalStatus.NotStarted,
                Summary = summary
            };
        }

        return new OnlineCourseStageState
        {
            Stage = stage,
            Provider = string.Join(" + ", steps.Select(step => step.Provider).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)),
            Status = AggregateStatuses(steps.Select(step => MapStatus(step.Status)).ToList()),
            Summary = summary,
            LastErrorMessage = FirstNonEmpty(steps.OrderByDescending(step => step.LastFailedAt ?? DateTime.MinValue).FirstOrDefault(step => !string.IsNullOrWhiteSpace(step.LastErrorMessage))?.LastErrorMessage),
            LastAttemptAt = GetLatestTimestamp(steps.Select(step => (DateTime?)step.CreatedAt).ToArray()),
            LastSucceededAt = GetLatestTimestamp(steps.Select(step => step.LastSucceededAt).ToArray()),
            LastFailedAt = GetLatestTimestamp(steps.Select(step => step.LastFailedAt).ToArray())
        };
    }

    private static OnlineCourseStageState ApplyArtifactStateHeuristic(
        OnlineCourseStageState stage,
        bool hasArtifact,
        string summary)
    {
        if (stage.Status == OnlineCourseOperationalStatus.NotStarted && hasArtifact)
        {
            stage.Status = OnlineCourseOperationalStatus.Partial;
            stage.Summary = summary;
        }
        else if (stage.Status == OnlineCourseOperationalStatus.Succeeded && !hasArtifact)
        {
            stage.Status = OnlineCourseOperationalStatus.Partial;
            stage.Summary = "A etapa foi marcada como concluida, mas o artefato esperado nao esta mais persistido.";
        }

        return stage;
    }

    private static OnlineCourseStageState ApplyStaleStageHeuristic(
        OnlineCourseStageState stage,
        DateTime? structureFreshAt,
        string summary,
        bool skipWhenStageSucceeded = true)
    {
        if (!structureFreshAt.HasValue || !stage.LastSucceededAt.HasValue)
        {
            return stage;
        }

        if (stage.LastSucceededAt.Value >= structureFreshAt.Value)
        {
            return stage;
        }

        if (skipWhenStageSucceeded && stage.Status == OnlineCourseOperationalStatus.NotStarted)
        {
            return stage;
        }

        stage.Status = OnlineCourseOperationalStatus.Partial;
        stage.Summary = summary;
        return stage;
    }

    private async Task SaveCourseAsync(Course course, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await CoursePersistenceHelper.UpsertCourseAsync(context, course, cancellationToken);
    }

    private static CoursePresentationRequestContract BuildPresentationRequest(Course course)
    {
        return new CoursePresentationRequestContract
        {
            Goal = "Gerar uma apresentacao amigavel para um curso online curado, sem alterar sua estrutura persistida no StudyHub.",
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

    private static CourseTextRefinementRequest BuildTextRefinementRequest(Course course)
    {
        return new CourseTextRefinementRequest
        {
            CourseId = course.Id,
            SourceType = course.SourceType.ToString(),
            CourseTitle = FirstNonEmpty(course.RawTitle, course.Title),
            CourseDescription = FirstNonEmpty(course.RawDescription, course.Description),
            Modules = course.Modules
                .OrderBy(module => module.Order)
                .Select(module => new ModuleTextRefinementInput
                {
                    ModuleId = module.Id,
                    Title = FirstNonEmpty(module.RawTitle, module.Title),
                    Description = FirstNonEmpty(module.RawDescription, module.Description),
                    Lessons = module.Topics
                        .OrderBy(topic => topic.Order)
                        .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
                        .Select(lesson => new LessonTextRefinementInput
                        {
                            LessonId = lesson.Id,
                            Title = FirstNonEmpty(lesson.RawTitle, lesson.Title),
                            Description = FirstNonEmpty(lesson.RawDescription, lesson.Description)
                        })
                        .ToList()
                })
                .ToList()
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

    private static void ApplyTextRefinement(Course course, CourseTextRefinementResponse response)
    {
        course.Title = FirstNonEmpty(response.RefinedCourseTitle, course.Title);
        course.Description = FirstNonEmpty(response.RefinedCourseDescription, course.Description);

        foreach (var module in course.Modules)
        {
            var refinedModule = response.Modules.FirstOrDefault(item => item.ModuleId == module.Id);
            if (refinedModule == null)
            {
                continue;
            }

            module.Title = FirstNonEmpty(refinedModule.Title, module.Title);
            module.Description = FirstNonEmpty(refinedModule.Description, module.Description);

            foreach (var lesson in module.Topics.SelectMany(topic => topic.Lessons))
            {
                var refinedLesson = refinedModule.Lessons.FirstOrDefault(item => item.LessonId == lesson.Id);
                if (refinedLesson == null)
                {
                    continue;
                }

                lesson.Title = FirstNonEmpty(refinedLesson.Title, lesson.Title);
                lesson.Description = FirstNonEmpty(refinedLesson.Description, lesson.Description);
            }
        }
    }

    private static void TouchCourseMetadata(Course course, params string[] completedSteps)
    {
        course.SourceMetadata.CompletedSteps = course.SourceMetadata.CompletedSteps
            .Concat(completedSteps)
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        course.SourceMetadata.LastEnrichedAt = DateTime.UtcNow;
    }

    private static OnlineCourseIntentRequest BuildIntentFromCourse(Course course)
    {
        return new OnlineCourseIntentRequest
        {
            CourseId = course.Id,
            Topic = FirstNonEmpty(course.SourceMetadata.RequestedTopic, course.RawTitle, course.Title),
            Objective = FirstNonEmpty(course.SourceMetadata.RequestedObjective, course.RawDescription, course.Description, "Atualizar a curadoria online existente."),
            Language = "pt-BR",
            PreferredProvider = FirstNonEmpty(course.SourceMetadata.Provider, "YouTube")
        };
    }

    private async Task RecordRunningStepAsync(
        Guid courseId,
        string stepKey,
        string provider,
        object payload,
        CancellationToken cancellationToken)
    {
        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = stepKey,
            Provider = provider,
            Status = CourseGenerationStepStatus.Running,
            RequestJson = IntegrationJsonHelper.Serialize(payload)
        }, cancellationToken);
    }

    private Task RecordFailedStepAsync(
        Guid courseId,
        string stepKey,
        string provider,
        object payload,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        return _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = stepKey,
            Provider = provider,
            Status = CourseGenerationStepStatus.Failed,
            RequestJson = IntegrationJsonHelper.Serialize(payload),
            ErrorMessage = errorMessage
        }, cancellationToken);
    }

    private async Task<CourseGenerationStepEntry?> GetStepStateAsync(Guid courseId, string stepKey, CancellationToken cancellationToken)
    {
        var stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(courseId, cancellationToken);
        return GetStepState(stepStates, stepKey);
    }

    private static CourseGenerationStepEntry? GetStepState(
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        string stepKey)
    {
        return stepStates.TryGetValue(stepKey, out var step) ? step : null;
    }

    private static IReadOnlyList<CourseGenerationStepEntry> GetOrderedStepStates(
        IReadOnlyDictionary<string, CourseGenerationStepEntry> stepStates,
        params string[] stepKeys)
    {
        return stepKeys
            .Select(stepKey => GetStepState(stepStates, stepKey))
            .Where(step => step != null)
            .Select(step => step!)
            .ToList();
    }

    private static OnlineCourseOperationalStatus AggregateStatuses(IReadOnlyCollection<OnlineCourseOperationalStatus> statuses)
    {
        if (statuses.Count == 0)
        {
            return OnlineCourseOperationalStatus.NotStarted;
        }

        if (statuses.Any(status => status == OnlineCourseOperationalStatus.Pending))
        {
            return statuses.All(status => status == OnlineCourseOperationalStatus.Pending)
                ? OnlineCourseOperationalStatus.Pending
                : OnlineCourseOperationalStatus.Partial;
        }

        if (statuses.Any(status => status == OnlineCourseOperationalStatus.Running))
        {
            return OnlineCourseOperationalStatus.Running;
        }

        var succeededCount = statuses.Count(status => status == OnlineCourseOperationalStatus.Succeeded);
        var failedCount = statuses.Count(status => status == OnlineCourseOperationalStatus.Failed);
        var skippedCount = statuses.Count(status => status == OnlineCourseOperationalStatus.Skipped);
        var notStartedCount = statuses.Count(status => status == OnlineCourseOperationalStatus.NotStarted);

        if (failedCount > 0 && (succeededCount > 0 || skippedCount > 0 || notStartedCount > 0))
        {
            return OnlineCourseOperationalStatus.Partial;
        }

        if (failedCount > 0)
        {
            return OnlineCourseOperationalStatus.Failed;
        }

        if (notStartedCount > 0 && (succeededCount > 0 || skippedCount > 0))
        {
            return OnlineCourseOperationalStatus.Partial;
        }

        if (succeededCount > 0 && notStartedCount == 0 && failedCount == 0)
        {
            return OnlineCourseOperationalStatus.Succeeded;
        }

        if (skippedCount > 0 && succeededCount == 0 && failedCount == 0 && notStartedCount == 0)
        {
            return OnlineCourseOperationalStatus.Skipped;
        }

        return OnlineCourseOperationalStatus.NotStarted;
    }

    private static OnlineCourseOperationalStatus MapStatus(CourseGenerationStepStatus status)
    {
        return status switch
        {
            CourseGenerationStepStatus.Pending => OnlineCourseOperationalStatus.Pending,
            CourseGenerationStepStatus.Running => OnlineCourseOperationalStatus.Running,
            CourseGenerationStepStatus.Succeeded => OnlineCourseOperationalStatus.Succeeded,
            CourseGenerationStepStatus.Failed => OnlineCourseOperationalStatus.Failed,
            CourseGenerationStepStatus.Skipped => OnlineCourseOperationalStatus.Skipped,
            _ => OnlineCourseOperationalStatus.NotStarted
        };
    }

    private static Guid? ResolveCurrentLessonId(CourseRecord courseRecord)
    {
        var orderedLessons = courseRecord.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList();

        return orderedLessons.FirstOrDefault(lesson => lesson.Status == studyhub.shared.Enums.LessonStatus.InProgress)?.Id
               ?? orderedLessons.LastOrDefault(lesson => lesson.Status == studyhub.shared.Enums.LessonStatus.Completed)?.Id;
    }

    private static string BuildSummaryFromStep(CourseGenerationStepEntry step, string defaultSummary)
    {
        return step.Status switch
        {
            CourseGenerationStepStatus.Pending => "Etapa pendente nesta geracao.",
            CourseGenerationStepStatus.Succeeded => defaultSummary,
            CourseGenerationStepStatus.Failed => FirstNonEmpty(step.LastErrorMessage, step.ErrorMessage, defaultSummary),
            CourseGenerationStepStatus.Skipped => FirstNonEmpty(step.ErrorMessage, "Etapa ignorada nesta execucao."),
            CourseGenerationStepStatus.Running => "Etapa em execucao.",
            _ => defaultSummary
        };
    }

    private static DateTime? GetLatestTimestamp(params DateTime?[] timestamps)
    {
        return timestamps
            .Where(value => value.HasValue)
            .OrderByDescending(value => value)
            .FirstOrDefault();
    }

    private static DateTime? GetLatestTimestamp(DateTime? timestamp, params IEnumerable<DateTime?>[] timestampSets)
    {
        return timestampSets
            .Prepend(new[] { timestamp })
            .SelectMany(set => set)
            .Where(value => value.HasValue)
            .OrderByDescending(value => value)
            .FirstOrDefault();
    }

    private static DateTime? GetLatestTimestamp(params IEnumerable<DateTime?>[] timestampSets)
    {
        return timestampSets
            .SelectMany(set => set)
            .Where(value => value.HasValue)
            .OrderByDescending(value => value)
            .FirstOrDefault();
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

    private static bool IsYouTubeUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<OnlineCourseStageState> BuildDefaultStageStates()
    {
        return Enum.GetValues<OnlineCourseOperationalStage>()
            .Select(stage => new OnlineCourseStageState
            {
                Stage = stage,
                Status = OnlineCourseOperationalStatus.NotStarted
            })
            .ToList();
    }

    private static OnlineCourseStageExecutionResult BuildFailureResult(
        Guid courseId,
        OnlineCourseOperationalStage stage,
        string message)
    {
        return new OnlineCourseStageExecutionResult
        {
            Success = false,
            CourseId = courseId,
            Stage = stage,
            Message = message
        };
    }
}
