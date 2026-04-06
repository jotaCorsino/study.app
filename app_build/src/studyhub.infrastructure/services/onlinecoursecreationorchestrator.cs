using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;
using studyhub.domain.AIContracts;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public sealed class OnlineCourseCreationOrchestrator(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IIntegrationSettingsService integrationSettingsService,
    IOnlineCuratedCourseBuilder onlineCuratedCourseBuilder,
    IGeminiCourseProvider geminiCourseProvider,
    CourseTextEnrichmentService courseTextEnrichmentService,
    IYouTubeDiscoveryProvider youTubeDiscoveryProvider,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    IRoadmapService roadmapService,
    IMaterialService materialService,
    ILogger<OnlineCourseCreationOrchestrator> logger) : IOnlineCourseCreationOrchestrator
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly IOnlineCuratedCourseBuilder _onlineCuratedCourseBuilder = onlineCuratedCourseBuilder;
    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly CourseTextEnrichmentService _courseTextEnrichmentService = courseTextEnrichmentService;
    private readonly IYouTubeDiscoveryProvider _youTubeDiscoveryProvider = youTubeDiscoveryProvider;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly IRoadmapService _roadmapService = roadmapService;
    private readonly IMaterialService _materialService = materialService;
    private readonly ILogger<OnlineCourseCreationOrchestrator> _logger = logger;

    public async Task<OnlineCourseCreationResult> CreateCourseAsync(OnlineCourseIntentRequest request, CancellationToken cancellationToken = default)
        => await RefreshCourseStructureAsync(request, refreshArtifacts: true, cancellationToken);

    public async Task<OnlineCourseCreationResult> RefreshCourseStructureAsync(
        OnlineCourseIntentRequest request,
        bool refreshArtifacts,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeIntentRequest(request, out var normalizedIntent, out var validationMessage))
        {
            return new OnlineCourseCreationResult
            {
                Success = false,
                Message = validationMessage
            };
        }

        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        if (!settings.HasGeminiKey || !settings.HasYouTubeKey)
        {
            return new OnlineCourseCreationResult
            {
                Success = false,
                Message = "Configure Gemini e YouTube nas configuracoes antes de criar ou atualizar um curso online."
            };
        }

        _logger.LogInformation(
            "Online course creation/update started. CourseId: {CourseId}. Topic: {Topic}. RefreshArtifacts: {RefreshArtifacts}",
            normalizedIntent.CourseId,
            normalizedIntent.Topic,
            refreshArtifacts);

        try
        {
            await InitializeOnlineCourseGenerationAsync(normalizedIntent.CourseId, refreshArtifacts, cancellationToken);
            var buildResult = await BuildCourseStructureAsync(normalizedIntent, settings.YouTubeRegionCode, cancellationToken);
            var persistedStructure = await ValidatePersistedStructureAsync(normalizedIntent.CourseId, cancellationToken);
            if (!persistedStructure.IsValid)
            {
                await RemoveInconsistentCourseAsync(normalizedIntent.CourseId, cancellationToken);

                return new OnlineCourseCreationResult
                {
                    Success = false,
                    CourseId = normalizedIntent.CourseId,
                    Message = persistedStructure.Message
                };
            }

            var structureCourse = persistedStructure.Course!;
            await TryRefreshPresentationAsync(structureCourse, cancellationToken);
            await TryRefreshTextRefinementAsync(structureCourse, cancellationToken);

            if (refreshArtifacts)
            {
                await _roadmapService.GenerateRoadmapAsync(normalizedIntent.CourseId);
                await _materialService.RefreshMaterialsAsync(normalizedIntent.CourseId);
            }

            var resultMessage = await BuildCreationOutcomeMessageAsync(normalizedIntent.CourseId, refreshArtifacts, cancellationToken);

            _logger.LogInformation(
                "Online course creation/update completed. CourseId: {CourseId}. CourseTitle: {CourseTitle}. RefreshArtifacts: {RefreshArtifacts}",
                normalizedIntent.CourseId,
                structureCourse.Title,
                refreshArtifacts);

            return new OnlineCourseCreationResult
            {
                Success = true,
                CourseId = normalizedIntent.CourseId,
                CourseTitle = structureCourse.Title,
                Message = resultMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Online course creation/update failed for intent {CourseId}.", normalizedIntent.CourseId);
            return new OnlineCourseCreationResult
            {
                Success = false,
                CourseId = normalizedIntent.CourseId,
                Message = ex.Message
            };
        }
    }

    private async Task<OnlineCuratedCourseBuildResult> BuildCourseStructureAsync(
        OnlineCourseIntentRequest normalizedIntent,
        string youTubeRegionCode,
        CancellationToken cancellationToken)
    {
        var planning = await ExecuteStepAsync(
            normalizedIntent.CourseId,
            OnlineCourseStepKeys.OnlinePlanning,
            "Gemini",
            normalizedIntent,
            () => _geminiCourseProvider.PlanOnlineCourseAsync(new OnlineCoursePlanningRequest
            {
                Intent = normalizedIntent
            }, cancellationToken),
            cancellationToken);

        var discovery = await ExecuteStepAsync(
            normalizedIntent.CourseId,
            OnlineCourseStepKeys.YouTubeDiscovery,
            "YouTube",
            new
            {
                normalizedIntent.Topic,
                normalizedIntent.Objective,
                planning.DiscoveryQueries
            },
            () => _youTubeDiscoveryProvider.DiscoverCourseSourcesAsync(new YouTubeCourseDiscoveryRequest
            {
                CourseId = normalizedIntent.CourseId,
                Topic = normalizedIntent.Topic,
                Objective = normalizedIntent.Objective,
                Queries = planning.DiscoveryQueries,
                RegionCode = youTubeRegionCode
            }, cancellationToken),
            cancellationToken);

        var curationRequest = new SourceCurationRequest
        {
            Intent = normalizedIntent,
            Planning = planning,
            Discovery = discovery
        };

        var curation = await ExecuteStepAsync(
            normalizedIntent.CourseId,
            OnlineCourseStepKeys.SourceCuration,
            "StudyHub",
            curationRequest,
            () => Task.FromResult(CurateSources(curationRequest)),
            cancellationToken);

        var textRefinementRequest = BuildOnlineRefinementRequest(normalizedIntent.CourseId, planning, curation);
        var textRefinement = await ExecuteStepAsync(
            normalizedIntent.CourseId,
            OnlineCourseStepKeys.TextRefinement,
            "Gemini",
            textRefinementRequest,
            () => _geminiCourseProvider.RefineCourseTextAsync(textRefinementRequest, cancellationToken),
            cancellationToken);

        var assemblyRequest = new OnlineCourseAssemblyRequest
        {
            Intent = normalizedIntent,
            Planning = planning,
            Curation = curation,
            TextRefinement = textRefinement
        };

        var buildResult = await ExecuteStepAsync(
            normalizedIntent.CourseId,
            OnlineCourseStepKeys.OnlineCourseAssembly,
            "StudyHub",
            assemblyRequest,
            () => _onlineCuratedCourseBuilder.BuildCourseAsync(assemblyRequest, cancellationToken),
            cancellationToken);

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = normalizedIntent.CourseId,
            StepKey = OnlineCourseStepKeys.CoursePersisted,
            Provider = "StudyHub",
            Status = CourseGenerationStepStatus.Running,
            RequestJson = IntegrationJsonHelper.Serialize(new
            {
                buildResult.Course.Id,
                buildResult.Course.SourceType,
                moduleCount = buildResult.Course.Modules.Count,
                lessonCount = buildResult.Course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count()
            })
        }, cancellationToken);

        await using (var context = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await CoursePersistenceHelper.UpsertCourseAsync(context, buildResult.Course, cancellationToken);
        }

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = normalizedIntent.CourseId,
            StepKey = OnlineCourseStepKeys.CoursePersisted,
            Provider = "StudyHub",
            Status = CourseGenerationStepStatus.Succeeded,
            RequestJson = IntegrationJsonHelper.Serialize(new
            {
                buildResult.Course.Id,
                buildResult.Course.SourceType,
                moduleCount = buildResult.Course.Modules.Count,
                lessonCount = buildResult.Course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count()
            }),
            ResponseJson = IntegrationJsonHelper.Serialize(new
            {
                buildResult.Course.SourceMetadata,
                moduleCount = buildResult.Course.Modules.Count,
                lessonCount = buildResult.Course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count()
            })
        }, cancellationToken);

        _logger.LogInformation(
            "Online course structure persisted. CourseId: {CourseId}. Modules: {ModuleCount}. Lessons: {LessonCount}",
            normalizedIntent.CourseId,
            buildResult.Course.Modules.Count,
            buildResult.Course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count());

        return buildResult;
    }

    private async Task TryRefreshPresentationAsync(Course course, CancellationToken cancellationToken)
    {
        var presentationRequest = BuildPresentationRequest(course);

        try
        {
            var presentation = await ExecuteStepAsync(
                course.Id,
                OnlineCourseStepKeys.CoursePresentation,
                "Gemini",
                presentationRequest,
                () => _geminiCourseProvider.GenerateCoursePresentationAsync(presentationRequest, cancellationToken),
                cancellationToken);

            ApplyPresentation(course, presentation);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await CoursePersistenceHelper.UpsertCourseAsync(context, course, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online course presentation refresh failed for course {CourseId}.", course.Id);
        }
    }

    private async Task TryRefreshTextRefinementAsync(Course course, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _courseTextEnrichmentService.RefineCourseAsync(
                course,
                OnlineCourseStepKeys.TextRefinement,
                forceRefresh: true,
                cancellationToken);

            if (!result.Updated && result.ProcessedBatches == 0)
            {
                return;
            }

            course.SourceMetadata.CompletedSteps = course.SourceMetadata.CompletedSteps
                .Concat(["OnlineCourseTextRefined"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            course.SourceMetadata.LastEnrichedAt = DateTime.UtcNow;

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await CoursePersistenceHelper.UpsertCourseAsync(context, course, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online course display text refinement failed after persistence for course {CourseId}.", course.Id);
        }
    }

    private static CoursePresentationRequestContract BuildPresentationRequest(Course course)
    {
        return new CoursePresentationRequestContract
        {
            Goal = "Enriquecer a apresentacao de um curso online curado sem alterar a ordem pedagogica das aulas persistidas no StudyHub.",
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

    private static bool TryNormalizeIntentRequest(
        OnlineCourseIntentRequest request,
        out OnlineCourseIntentRequest normalizedIntent,
        out string validationMessage)
    {
        validationMessage = string.Empty;
        normalizedIntent = new OnlineCourseIntentRequest();

        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            validationMessage = "Informe o tema principal do curso.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            validationMessage = "Informe o objetivo principal do curso.";
            return false;
        }

        normalizedIntent = new OnlineCourseIntentRequest
        {
            CourseId = request.CourseId == Guid.Empty ? Guid.NewGuid() : request.CourseId,
            Topic = request.Topic.Trim(),
            Objective = request.Objective.Trim(),
            Language = string.IsNullOrWhiteSpace(request.Language) ? "pt-BR" : request.Language.Trim(),
            PreferredProvider = string.IsNullOrWhiteSpace(request.PreferredProvider) ? "YouTube" : request.PreferredProvider.Trim()
        };

        return true;
    }

    private async Task InitializeOnlineCourseGenerationAsync(Guid courseId, bool refreshArtifacts, CancellationToken cancellationToken)
    {
        var stepKeys = new List<string>
        {
            OnlineCourseStepKeys.OnlinePlanning,
            OnlineCourseStepKeys.YouTubeDiscovery,
            OnlineCourseStepKeys.SourceCuration,
            OnlineCourseStepKeys.TextRefinement,
            OnlineCourseStepKeys.OnlineCourseAssembly,
            OnlineCourseStepKeys.CoursePersisted,
            OnlineCourseStepKeys.OnlineCourseValidation,
            OnlineCourseStepKeys.CoursePresentation
        };

        if (refreshArtifacts)
        {
            stepKeys.Add(OnlineCourseStepKeys.RoadmapGeneration);
            stepKeys.Add(OnlineCourseStepKeys.SupplementaryQueryGeneration);
            stepKeys.Add(OnlineCourseStepKeys.SupplementaryYouTubeDiscovery);
            stepKeys.Add(OnlineCourseStepKeys.SupplementaryMaterialsGeneration);
        }

        foreach (var stepKey in stepKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = stepKey,
                Provider = ResolveProviderForStep(stepKey),
                Status = CourseGenerationStepStatus.Pending
            }, cancellationToken);
        }
    }

    private async Task<PersistedOnlineCourseValidationResult> ValidatePersistedStructureAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.OnlineCourseValidation,
            Provider = "StudyHub",
            Status = CourseGenerationStepStatus.Running
        }, cancellationToken);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var persistedCourse = await context.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);

        if (persistedCourse == null)
        {
            return await RecordValidationFailureAsync(courseId, "O curso online nao foi encontrado no banco apos a persistencia.", cancellationToken);
        }

        if (persistedCourse.SourceType != CourseSourceType.OnlineCurated)
        {
            return await RecordValidationFailureAsync(courseId, "O curso persistido nao manteve a origem OnlineCurated esperada.", cancellationToken);
        }

        var orderedModules = persistedCourse.Modules
            .OrderBy(module => module.Order)
            .ToList();
        var orderedLessons = orderedModules
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList();

        if (orderedModules.Count == 0)
        {
            return await RecordValidationFailureAsync(courseId, "O curso online foi persistido sem modulos.", cancellationToken);
        }

        if (orderedLessons.Count == 0)
        {
            return await RecordValidationFailureAsync(courseId, "O curso online foi persistido sem aulas.", cancellationToken);
        }

        if (orderedLessons.Any(lesson => lesson.SourceType != LessonSourceType.ExternalVideo || string.IsNullOrWhiteSpace(lesson.ExternalUrl)))
        {
            return await RecordValidationFailureAsync(courseId, "O curso online foi persistido com aulas externas invalidas ou sem URL.", cancellationToken);
        }

        var hasModuleOrderGaps = orderedModules
            .Select((module, index) => module.Order != index + 1)
            .Any(hasGap => hasGap);
        var hasLessonOrderGaps = orderedModules
            .SelectMany(module => module.Topics)
            .Any(topic => topic.Lessons
                .OrderBy(lesson => lesson.Order)
                .Select((lesson, index) => lesson.Order != index + 1)
                .Any(hasGap => hasGap));

        if (hasModuleOrderGaps || hasLessonOrderGaps)
        {
            return await RecordValidationFailureAsync(courseId, "O curso online foi persistido com ordem incoerente de modulos ou aulas.", cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(persistedCourse.SourceMetadataJson))
        {
            return await RecordValidationFailureAsync(courseId, "O curso online foi persistido sem metadados de origem.", cancellationToken);
        }

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.OnlineCourseValidation,
            Provider = "StudyHub",
            Status = CourseGenerationStepStatus.Succeeded,
            ResponseJson = IntegrationJsonHelper.Serialize(new
            {
                moduleCount = orderedModules.Count,
                lessonCount = orderedLessons.Count,
                sourceType = persistedCourse.SourceType,
                currentLessonId = persistedCourse.CurrentLessonId
            })
        }, cancellationToken);

        return new PersistedOnlineCourseValidationResult
        {
            IsValid = true,
            Course = persistedCourse.ToDomain()
        };
    }

    private async Task<PersistedOnlineCourseValidationResult> RecordValidationFailureAsync(Guid courseId, string message, CancellationToken cancellationToken)
    {
        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.OnlineCourseValidation,
            Provider = "StudyHub",
            Status = CourseGenerationStepStatus.Failed,
            ErrorMessage = message
        }, cancellationToken);

        return new PersistedOnlineCourseValidationResult
        {
            IsValid = false,
            Message = message
        };
    }

    private async Task RemoveInconsistentCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.Courses.FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);
        if (record == null)
        {
            return;
        }

        context.Courses.Remove(record);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> BuildCreationOutcomeMessageAsync(Guid courseId, bool refreshArtifacts, CancellationToken cancellationToken)
    {
        var stepStates = await _courseGenerationHistoryService.GetStepStatesAsync(courseId, cancellationToken);
        var warnings = new List<string>();

        if (stepStates.TryGetValue(OnlineCourseStepKeys.CoursePresentation, out var presentationStep) &&
            presentationStep.Status == CourseGenerationStepStatus.Failed)
        {
            warnings.Add("a apresentacao do curso ficou pendente");
        }

        if (stepStates.TryGetValue(OnlineCourseStepKeys.TextRefinement, out var textRefinementStep) &&
            textRefinementStep.Status == CourseGenerationStepStatus.Failed)
        {
            warnings.Add("o refinamento textual ficou parcial");
        }

        if (refreshArtifacts &&
            stepStates.TryGetValue(OnlineCourseStepKeys.RoadmapGeneration, out var roadmapStep) &&
            roadmapStep.Status == CourseGenerationStepStatus.Failed)
        {
            warnings.Add("o roadmap falhou");
        }

        if (refreshArtifacts &&
            stepStates.TryGetValue(OnlineCourseStepKeys.SupplementaryMaterialsGeneration, out var materialsStep) &&
            materialsStep.Status == CourseGenerationStepStatus.Failed)
        {
            warnings.Add("os materiais complementares falharam");
        }

        if (warnings.Count == 0)
        {
            return refreshArtifacts
                ? "Curso online criado e persistido com sucesso."
                : "Estrutura do curso online atualizada com sucesso.";
        }

        return $"Curso online criado com estrutura base valida, mas {string.Join(" e ", warnings)} nesta execucao.";
    }

    private static string ResolveProviderForStep(string stepKey)
    {
        return stepKey switch
        {
            OnlineCourseStepKeys.OnlinePlanning => "Gemini",
            OnlineCourseStepKeys.YouTubeDiscovery => "YouTube",
            OnlineCourseStepKeys.SourceCuration => "StudyHub",
            OnlineCourseStepKeys.TextRefinement => "Gemini",
            OnlineCourseStepKeys.CoursePresentation => "Gemini",
            OnlineCourseStepKeys.OnlineCourseAssembly => "StudyHub",
            OnlineCourseStepKeys.CoursePersisted => "StudyHub",
            OnlineCourseStepKeys.OnlineCourseValidation => "StudyHub",
            OnlineCourseStepKeys.RoadmapGeneration => "Gemini",
            OnlineCourseStepKeys.SupplementaryQueryGeneration => "Gemini",
            OnlineCourseStepKeys.SupplementaryYouTubeDiscovery => "YouTube",
            OnlineCourseStepKeys.SupplementaryMaterialsGeneration => "StudyHub",
            _ => "StudyHub"
        };
    }

    private SourceCurationResponse CurateSources(SourceCurationRequest request)
    {
        var bundlesByPlaylistId = request.Discovery.PlaylistBundles
            .ToDictionary(bundle => bundle.PlaylistId, StringComparer.OrdinalIgnoreCase);

        var selectedSources = new Dictionary<string, YouTubeSourceCandidate>(StringComparer.OrdinalIgnoreCase);
        var moduleSelections = new List<CuratedModuleSelection>();
        var usedLessonIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var intentTokens = BuildIntentTokens(request.Intent.Topic, request.Intent.Objective);
        var anchorCandidates = request.Discovery.Candidates
            .Where(candidate => string.Equals(candidate.SourceKind, "playlist", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => ComputeAnchorScore(candidate, intentTokens))
            .ThenByDescending(candidate => candidate.ItemCount)
            .Take(2)
            .ToList();

        if (anchorCandidates.Count == 0)
        {
            anchorCandidates = request.Discovery.Candidates
                .OrderByDescending(candidate => ComputeAnchorScore(candidate, intentTokens))
                .Take(2)
                .ToList();
        }

        foreach (var modulePlan in request.Planning.Modules.OrderBy(module => module.Order))
        {
            var moduleTokens = BuildModuleTokens(modulePlan, intentTokens);
            var assignments = new List<CuratedSourceAssignment>();

            foreach (var anchorCandidate in anchorCandidates)
            {
                var anchorLessons = SelectCandidateLessonsForModule(
                    anchorCandidate,
                    moduleTokens,
                    bundlesByPlaylistId,
                    usedLessonIds,
                    maxLessons: 10,
                    requirePositiveScore: true);

                if (anchorLessons.Count == 0)
                {
                    continue;
                }

                assignments.Add(CreateAssignment(anchorCandidate, anchorLessons));
                TrackSelectedSource(selectedSources, anchorCandidate);
                RegisterUsedLessons(usedLessonIds, anchorLessons);
            }

            var supplementalCandidates = request.Discovery.Candidates
                .OrderByDescending(candidate => ComputeModuleSourceScore(candidate, moduleTokens, bundlesByPlaylistId, usedLessonIds))
                .ThenByDescending(candidate => candidate.ItemCount)
                .ToList();

            foreach (var candidate in supplementalCandidates)
            {
                if (assignments.Any(item =>
                        string.Equals(item.SourceKind, candidate.SourceKind, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.SourceId, candidate.SourceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var lessons = SelectCandidateLessonsForModule(
                    candidate,
                    moduleTokens,
                    bundlesByPlaylistId,
                    usedLessonIds,
                    maxLessons: candidate.SourceKind == "playlist" ? 8 : 2,
                    requirePositiveScore: assignments.Count == 0);

                if (lessons.Count == 0)
                {
                    continue;
                }

                assignments.Add(CreateAssignment(candidate, lessons));
                TrackSelectedSource(selectedSources, candidate);
                RegisterUsedLessons(usedLessonIds, lessons);

                if (HasEnoughModuleCoverage(assignments))
                {
                    break;
                }
            }

            if (assignments.Count == 0)
            {
                var fallbackCandidate = request.Discovery.Candidates
                    .OrderByDescending(candidate => ComputeModuleSourceScore(candidate, moduleTokens, bundlesByPlaylistId, usedLessonIds))
                    .FirstOrDefault();

                if (fallbackCandidate == null)
                {
                    continue;
                }

                var fallbackLessons = SelectCandidateLessonsForModule(
                    fallbackCandidate,
                    moduleTokens,
                    bundlesByPlaylistId,
                    usedLessonIds,
                    maxLessons: fallbackCandidate.SourceKind == "playlist" ? 8 : 1,
                    requirePositiveScore: false);

                if (fallbackLessons.Count == 0)
                {
                    continue;
                }

                assignments.Add(CreateAssignment(fallbackCandidate, fallbackLessons));
                TrackSelectedSource(selectedSources, fallbackCandidate);
                RegisterUsedLessons(usedLessonIds, fallbackLessons);
            }

            moduleSelections.Add(new CuratedModuleSelection
            {
                Order = modulePlan.Order,
                ModuleTitle = modulePlan.Title,
                ModuleObjective = modulePlan.Objective,
                Sources = assignments
            });
        }

        if (moduleSelections.Count == 0)
        {
            var fallbackCandidates = request.Discovery.Candidates
                .OrderByDescending(candidate => ComputeAnchorScore(candidate, intentTokens))
                .Take(3)
                .ToList();

            var fallbackAssignments = fallbackCandidates
                .Select(candidate =>
                {
                    var lessons = SelectCandidateLessonsForModule(
                        candidate,
                        intentTokens,
                        bundlesByPlaylistId,
                        usedLessonIds,
                        maxLessons: candidate.SourceKind == "playlist" ? 10 : 2,
                        requirePositiveScore: false);

                    if (lessons.Count == 0)
                    {
                        return null;
                    }

                    RegisterUsedLessons(usedLessonIds, lessons);
                    TrackSelectedSource(selectedSources, candidate);

                    return CreateAssignment(candidate, lessons);
                })
                .Where(assignment => assignment != null)
                .Select(assignment => assignment!)
                .ToList();

            if (fallbackAssignments.Count > 0)
            {
                moduleSelections.Add(new CuratedModuleSelection
                {
                    Order = 1,
                    ModuleTitle = FirstNonEmpty(request.Planning.Modules.FirstOrDefault()?.Title, request.Intent.Topic),
                    ModuleObjective = FirstNonEmpty(request.Planning.Modules.FirstOrDefault()?.Objective, request.Intent.Objective),
                    Sources = fallbackAssignments
                });
            }
        }

        var anchorSummary = anchorCandidates.Count == 0
            ? "sem playlist ancora dominante"
            : string.Join(", ", anchorCandidates.Select(candidate => candidate.Title).Distinct(StringComparer.OrdinalIgnoreCase));

        return new SourceCurationResponse
        {
            Summary = $"Curadoria concluida com {moduleSelections.Count} modulos, {selectedSources.Count} fontes selecionadas, playlist(s) ancora {anchorSummary} e complementacao por lacunas de cobertura.",
            SelectedQueries = request.Planning.DiscoveryQueries,
            SelectedSources = selectedSources.Values.ToList(),
            ModuleSelections = moduleSelections
        };
    }

    private static double ComputeAnchorScore(YouTubeSourceCandidate candidate, IReadOnlyCollection<string> intentTokens)
    {
        var textScore = ComputeTokenOverlapScore($"{candidate.Title} {candidate.Description} {candidate.ChannelTitle}", intentTokens);
        var playlistBonus = string.Equals(candidate.SourceKind, "playlist", StringComparison.OrdinalIgnoreCase) ? 30d : 0d;
        var coverageBonus = Math.Min(25d, candidate.ItemCount * 1.5d);
        var durationBonus = Math.Min(15d, candidate.Duration.TotalMinutes / 12d);
        return candidate.RelevanceScore + candidate.AuthorityScore + playlistBonus + coverageBonus + durationBonus + textScore;
    }

    private static double ComputeModuleSourceScore(
        YouTubeSourceCandidate candidate,
        IReadOnlyCollection<string> moduleTokens,
        IReadOnlyDictionary<string, YouTubePlaylistBundle> bundlesByPlaylistId,
        IReadOnlySet<string> usedLessonIds)
    {
        var lessons = ResolveCandidateLessons(candidate, bundlesByPlaylistId)
            .Where(lesson => !usedLessonIds.Contains(GetLessonKey(lesson)))
            .ToList();

        if (lessons.Count == 0)
        {
            return double.MinValue;
        }

        var lessonCoverageScore = lessons
            .Take(10)
            .Select(lesson => ComputeLessonScore(lesson, moduleTokens))
            .DefaultIfEmpty(0d)
            .Average();

        var availableDurationBonus = Math.Min(20d, lessons.Sum(lesson => lesson.Duration.TotalMinutes) / 15d);
        var playlistBonus = string.Equals(candidate.SourceKind, "playlist", StringComparison.OrdinalIgnoreCase) ? 15d : 0d;
        return candidate.RelevanceScore + candidate.AuthorityScore + lessonCoverageScore + availableDurationBonus + playlistBonus;
    }

    private static IReadOnlyList<YouTubeVideoDescriptor> SelectCandidateLessonsForModule(
        YouTubeSourceCandidate candidate,
        IReadOnlyCollection<string> moduleTokens,
        IReadOnlyDictionary<string, YouTubePlaylistBundle> bundlesByPlaylistId,
        IReadOnlySet<string> usedLessonIds,
        int maxLessons,
        bool requirePositiveScore)
    {
        var rankedLessons = ResolveCandidateLessons(candidate, bundlesByPlaylistId)
            .Where(lesson => !usedLessonIds.Contains(GetLessonKey(lesson)))
            .Select(lesson => new
            {
                Lesson = lesson,
                Score = ComputeLessonScore(lesson, moduleTokens)
            })
            .Where(item => !requirePositiveScore || item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Lesson.OrderHint)
            .ThenBy(item => item.Lesson.Title)
            .Take(Math.Max(1, maxLessons))
            .OrderBy(item => item.Lesson.OrderHint)
            .ThenBy(item => item.Lesson.Title)
            .Select(item => item.Lesson)
            .ToList();

        if (rankedLessons.Count > 0)
        {
            return rankedLessons;
        }

        if (requirePositiveScore)
        {
            return [];
        }

        return ResolveCandidateLessons(candidate, bundlesByPlaylistId)
            .Where(lesson => !usedLessonIds.Contains(GetLessonKey(lesson)))
            .OrderBy(lesson => lesson.OrderHint)
            .ThenBy(lesson => lesson.Title)
            .Take(Math.Max(1, maxLessons))
            .ToList();
    }

    private static IReadOnlyList<YouTubeVideoDescriptor> ResolveCandidateLessons(
        YouTubeSourceCandidate candidate,
        IReadOnlyDictionary<string, YouTubePlaylistBundle> bundlesByPlaylistId)
    {
        if (string.Equals(candidate.SourceKind, "playlist", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.PlaylistId) &&
            bundlesByPlaylistId.TryGetValue(candidate.PlaylistId, out var bundle))
        {
            return bundle.Videos;
        }

        return
        [
            new YouTubeVideoDescriptor
            {
                VideoId = candidate.VideoId,
                Title = candidate.Title,
                Description = candidate.Description,
                Url = candidate.Url,
                ThumbnailUrl = candidate.ThumbnailUrl,
                ChannelId = candidate.ChannelId,
                ChannelTitle = candidate.ChannelTitle,
                Duration = candidate.Duration,
                RelevanceScore = candidate.RelevanceScore
            }
        ];
    }

    private static CuratedSourceAssignment CreateAssignment(YouTubeSourceCandidate candidate, IReadOnlyList<YouTubeVideoDescriptor> lessons)
    {
        return new CuratedSourceAssignment
        {
            SourceKind = candidate.SourceKind,
            SourceId = candidate.SourceId,
            Title = candidate.Title,
            Url = candidate.Url,
            PlaylistId = candidate.PlaylistId,
            ChannelTitle = candidate.ChannelTitle,
            AuthorityScore = candidate.AuthorityScore,
            RelevanceScore = candidate.RelevanceScore,
            Lessons = lessons
        };
    }

    private static void TrackSelectedSource(IDictionary<string, YouTubeSourceCandidate> selectedSources, YouTubeSourceCandidate candidate)
    {
        selectedSources[$"{candidate.SourceKind}:{candidate.SourceId}"] = candidate;
    }

    private static void RegisterUsedLessons(ISet<string> usedLessonIds, IReadOnlyList<YouTubeVideoDescriptor> lessons)
    {
        foreach (var lesson in lessons)
        {
            usedLessonIds.Add(GetLessonKey(lesson));
        }
    }

    private static bool HasEnoughModuleCoverage(IReadOnlyList<CuratedSourceAssignment> assignments)
    {
        var totalLessons = assignments.SelectMany(assignment => assignment.Lessons).Count();
        if (totalLessons >= 4)
        {
            return true;
        }

        var totalDurationMinutes = assignments
            .SelectMany(assignment => assignment.Lessons)
            .Sum(lesson => lesson.Duration.TotalMinutes);

        return totalDurationMinutes >= 45;
    }

    private static IReadOnlyCollection<string> BuildIntentTokens(string topic, string objective)
    {
        return Tokenize($"{topic} {objective}");
    }

    private static IReadOnlyCollection<string> BuildModuleTokens(OnlineCourseModulePlan modulePlan, IReadOnlyCollection<string> intentTokens)
    {
        return intentTokens
            .Concat(Tokenize(modulePlan.Title))
            .Concat(Tokenize(modulePlan.Objective))
            .Concat(Tokenize(modulePlan.Description))
            .Concat(modulePlan.SearchQueries.SelectMany(Tokenize))
            .Concat(modulePlan.Keywords.SelectMany(Tokenize))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyCollection<string> Tokenize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([' ', ',', ';', ':', '-', '_', '/', '\\', '|', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => token.Trim().ToLowerInvariant())
                .Where(token => token.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static double ComputeLessonScore(YouTubeVideoDescriptor lesson, IReadOnlyCollection<string> moduleTokens)
    {
        return ComputeTokenOverlapScore($"{lesson.Title} {lesson.Description} {lesson.ChannelTitle}", moduleTokens);
    }

    private static double ComputeTokenOverlapScore(string value, IReadOnlyCollection<string> tokens)
    {
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var lowerValue = value.ToLowerInvariant();
        var tokenHits = tokens.Count(token => lowerValue.Contains(token, StringComparison.Ordinal));
        return tokenHits * 8d;
    }

    private static string GetLessonKey(YouTubeVideoDescriptor lesson)
    {
        return !string.IsNullOrWhiteSpace(lesson.VideoId)
            ? lesson.VideoId
            : lesson.Url;
    }

    private static CourseTextRefinementRequest BuildOnlineRefinementRequest(
        Guid courseId,
        OnlineCoursePlanningResponse planning,
        SourceCurationResponse curation)
    {
        var modules = curation.ModuleSelections
            .OrderBy(selection => selection.Order)
            .Select(selection =>
            {
                var moduleId = CourseIdentityHelper.CreateModuleId(courseId, selection.Order);
                var lessonOrder = 1;
                var lessons = selection.Sources
                    .SelectMany(source => source.Lessons.Count > 0
                        ? source.Lessons
                        : [new YouTubeVideoDescriptor
                        {
                            VideoId = source.SourceId,
                            Title = source.Title,
                            Url = source.Url
                        }])
                    .GroupBy(lesson => !string.IsNullOrWhiteSpace(lesson.VideoId) ? lesson.VideoId : lesson.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        var lesson = group.First();
                        var lessonId = CourseIdentityHelper.CreateLessonId(courseId, selection.Order, lessonOrder++, !string.IsNullOrWhiteSpace(lesson.VideoId) ? lesson.VideoId : lesson.Url);
                        return new LessonTextRefinementInput
                        {
                            LessonId = lessonId,
                            Title = lesson.Title,
                            Description = lesson.Description
                        };
                    })
                    .ToList();

                return new ModuleTextRefinementInput
                {
                    ModuleId = moduleId,
                    Title = selection.ModuleTitle,
                    Description = FirstNonEmpty(
                        planning.Modules.FirstOrDefault(item => item.Order == selection.Order)?.Description,
                        selection.ModuleObjective),
                    Lessons = lessons
                };
            })
            .ToList();

        return new CourseTextRefinementRequest
        {
            CourseId = courseId,
            SourceType = CourseSourceType.OnlineCurated.ToString(),
            CourseTitle = planning.FriendlyTitle,
            CourseDescription = planning.CourseDescription,
            Modules = modules
        };
    }

    private async Task<TResponse> ExecuteStepAsync<TRequest, TResponse>(
        Guid courseId,
        string stepKey,
        string provider,
        TRequest request,
        Func<Task<TResponse>> action,
        CancellationToken cancellationToken)
    {
        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = stepKey,
            Provider = provider,
            Status = CourseGenerationStepStatus.Running,
            RequestJson = IntegrationJsonHelper.Serialize(request)
        }, cancellationToken);

        try
        {
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

    private sealed class PersistedOnlineCourseValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public Course? Course { get; set; }
    }
}
