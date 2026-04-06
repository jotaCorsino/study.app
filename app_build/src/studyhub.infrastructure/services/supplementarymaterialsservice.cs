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

public sealed class SupplementaryMaterialsService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    ICourseService courseService,
    IIntegrationSettingsService integrationSettingsService,
    IGeminiCourseProvider geminiCourseProvider,
    IYouTubeDiscoveryProvider youTubeDiscoveryProvider,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    ILogger<SupplementaryMaterialsService> logger) : IMaterialService, ISupplementaryMaterialsService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ICourseService _courseService = courseService;
    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly IYouTubeDiscoveryProvider _youTubeDiscoveryProvider = youTubeDiscoveryProvider;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly ILogger<SupplementaryMaterialsService> _logger = logger;

    public async Task<List<Material>> GetMaterialsByCourseAsync(Guid courseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await context.CourseMaterials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == courseId);

        return record == null
            ? []
            : PersistenceMapper.DeserializeMaterials(record.MaterialsJson);
    }

    public async Task RefreshMaterialsAsync(Guid courseId)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync();
        if (!settings.HasGeminiKey || !settings.HasYouTubeKey)
        {
            _logger.LogWarning("Supplementary material generation skipped because Gemini/YouTube keys are missing. CourseId: {CourseId}", courseId);
            await RecordSkippedAsync(courseId, "Chaves de Gemini/YouTube ausentes.");
            return;
        }

        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            _logger.LogWarning("Supplementary material generation skipped because the course was not found. CourseId: {CourseId}", courseId);
            return;
        }

        _logger.LogInformation(
            "Supplementary material generation started. CourseId: {CourseId}. SourceType: {SourceType}",
            courseId,
            course.SourceType);

        var generationContext = await BuildGenerationContextAsync(courseId, course, CancellationToken.None);
        var aiRequest = generationContext.AiRequest;

        try
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryMaterialsGeneration,
                Provider = "StudyHub",
                Status = CourseGenerationStepStatus.Running,
                RequestJson = IntegrationJsonHelper.Serialize(aiRequest)
            });

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryQueryGeneration,
                Provider = "Gemini",
                Status = CourseGenerationStepStatus.Running,
                RequestJson = IntegrationJsonHelper.Serialize(aiRequest)
            });

            var aiResponse = await _geminiCourseProvider.GenerateSupplementaryQueriesAsync(aiRequest);
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryQueryGeneration,
                Provider = "Gemini",
                Status = CourseGenerationStepStatus.Succeeded,
                RequestJson = IntegrationJsonHelper.Serialize(aiRequest),
                ResponseJson = IntegrationJsonHelper.Serialize(aiResponse)
            });

            var discoveryRequest = new YoutubeMaterialDiscoveryRequest
            {
                CourseId = generationContext.DiscoveryRequest.CourseId,
                CourseTitle = generationContext.DiscoveryRequest.CourseTitle,
                CourseDescription = generationContext.DiscoveryRequest.CourseDescription,
                ModuleTitles = generationContext.DiscoveryRequest.ModuleTitles,
                LessonTitles = generationContext.DiscoveryRequest.LessonTitles,
                SelectedSources = generationContext.DiscoveryRequest.SelectedSources,
                SeedQueries = generationContext.DiscoveryRequest.SeedQueries
                    .Concat(aiResponse.RecommendedSearchQueries)
                    .Where(query => !string.IsNullOrWhiteSpace(query))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryYouTubeDiscovery,
                Provider = "YouTube",
                Status = CourseGenerationStepStatus.Running,
                RequestJson = IntegrationJsonHelper.Serialize(discoveryRequest)
            });

            var discoveryResponse = await _youTubeDiscoveryProvider.DiscoverMaterialsAsync(discoveryRequest);
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryYouTubeDiscovery,
                Provider = "YouTube",
                Status = CourseGenerationStepStatus.Succeeded,
                RequestJson = IntegrationJsonHelper.Serialize(discoveryRequest),
                ResponseJson = IntegrationJsonHelper.Serialize(discoveryResponse)
            });

            var materials = MapMaterials(courseId, discoveryResponse, aiResponse.RecommendedSearchQueries);

            await using var context = await _contextFactory.CreateDbContextAsync();
            await SaveMaterialsInternalAsync(context, courseId, materials);

            var courseRecord = await context.Courses.FirstOrDefaultAsync(item => item.Id == courseId);
            if (courseRecord != null)
            {
                var metadata = string.IsNullOrWhiteSpace(courseRecord.SourceMetadataJson)
                    ? new CourseSourceMetadata()
                    : System.Text.Json.JsonSerializer.Deserialize<CourseSourceMetadata>(courseRecord.SourceMetadataJson, IntegrationJsonHelper.JsonOptions) ?? new CourseSourceMetadata();

                metadata.SearchQueries = metadata.SearchQueries
                    .Concat(aiResponse.RecommendedSearchQueries)
                    .Where(query => !string.IsNullOrWhiteSpace(query))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                metadata.SourceUrls = metadata.SourceUrls
                    .Concat(materials.Select(material => material.Url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                metadata.VideoIds = metadata.VideoIds
                    .Concat(materials.Select(material => material.VideoId))
                    .Where(videoId => !string.IsNullOrWhiteSpace(videoId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                metadata.CompletedSteps = metadata.CompletedSteps
                    .Concat(["SupplementaryMaterialsGenerated"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                metadata.LastEnrichedAt = DateTime.UtcNow;

                courseRecord.SourceMetadataJson = PersistenceMapper.SerializeCourseSourceMetadata(metadata);
                await context.SaveChangesAsync();
            }

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryMaterialsGeneration,
                Provider = "StudyHub",
                Status = CourseGenerationStepStatus.Succeeded,
                RequestJson = IntegrationJsonHelper.Serialize(new SupplementaryMaterialsGenerationRequest
                {
                    CourseId = courseId,
                    CourseTitle = course.Title,
                    CourseDescription = course.Description,
                    ModuleTitles = course.Modules.Select(module => module.Title).ToList(),
                    SeedQueries = aiResponse.RecommendedSearchQueries
                }),
                ResponseJson = IntegrationJsonHelper.Serialize(new SupplementaryMaterialsGenerationResponse
                {
                    Materials = materials,
                    Queries = aiResponse.RecommendedSearchQueries,
                    Notes = aiResponse.CurationNotes
                })
            });

            _logger.LogInformation(
                "Supplementary material generation completed. CourseId: {CourseId}. Materials: {MaterialCount}",
                courseId,
                materials.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supplementary material generation failed for course {CourseId}.", courseId);
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.SupplementaryMaterialsGeneration,
                Provider = "StudyHub",
                Status = CourseGenerationStepStatus.Failed,
                ErrorMessage = ex.Message
            });
        }
    }

    private async Task<SupplementaryGenerationContext> BuildGenerationContextAsync(Guid courseId, Course course, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var roadmapRecord = await context.CourseRoadmaps
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == courseId, cancellationToken);
        var roadmapHighlights = roadmapRecord == null
            ? []
            : ExtractRoadmapHighlights(PersistenceMapper.DeserializeRoadmap(roadmapRecord.LevelsJson));

        var metadata = course.SourceMetadata ?? new CourseSourceMetadata();
        var orderedModules = course.Modules.OrderBy(module => module.Order).ToList();
        var orderedLessons = orderedModules
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList();

        var aiRequest = new CourseSupplementaryMaterialsRequestContract
        {
            CurationGoal = course.SourceType == CourseSourceType.OnlineCurated
                ? "Gerar conteudo gratuito complementar para um curso online curado, cobrindo lacunas, aprofundando temas relevantes e evitando repetir a trilha principal."
                : "Gerar conteudo gratuito complementar para um curso local importado, aprofundando os temas detectados sem substituir a estrutura principal do disco.",
            CourseInformation = new SupplementaryMatchInformationContract
            {
                SourceType = course.SourceType.ToString(),
                Title = course.Title,
                Description = course.Description,
                RequestedTopic = metadata.RequestedTopic,
                RequestedObjective = metadata.RequestedObjective,
                Modules = orderedModules
                    .Select(module => $"{FirstNonEmpty(module.Title, module.RawTitle)} | {FirstNonEmpty(module.Description, module.RawDescription)}")
                    .ToList(),
                LessonHighlights = orderedLessons
                    .Select(lesson => FirstNonEmpty(lesson.Title, lesson.RawTitle))
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .Take(60)
                    .ToList(),
                RoadmapHighlights = roadmapHighlights,
                SelectedSources = metadata.CuratedSources
                    .Select(source => $"{source.SourceKind}: {source.Title} | {source.ChannelTitle}")
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Take(20)
                    .ToList(),
                ExistingQueries = metadata.SearchQueries
                    .Where(query => !string.IsNullOrWhiteSpace(query))
                    .Take(20)
                    .ToList()
            }
        };

        var discoveryRequest = new YoutubeMaterialDiscoveryRequest
        {
            CourseId = courseId,
            CourseTitle = course.Title,
            CourseDescription = course.Description,
            ModuleTitles = orderedModules
                .Select(module => FirstNonEmpty(module.Title, module.RawTitle))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList(),
            LessonTitles = orderedLessons
                .Select(lesson => FirstNonEmpty(lesson.Title, lesson.RawTitle))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Take(80)
                .ToList(),
            SeedQueries = metadata.SearchQueries
                .Where(query => !string.IsNullOrWhiteSpace(query))
                .Take(20)
                .ToList(),
            SelectedSources = metadata.CuratedSources
                .Select(source => FirstNonEmpty(source.Title, source.ChannelTitle))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(20)
                .ToList()
        };

        return new SupplementaryGenerationContext(aiRequest, discoveryRequest);
    }

    private async Task RecordSkippedAsync(Guid courseId, string reason)
    {
        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.SupplementaryMaterialsGeneration,
            Provider = "StudyHub",
            Status = CourseGenerationStepStatus.Skipped,
            ErrorMessage = reason
        });
    }

    private static List<Material> MapMaterials(
        Guid courseId,
        YoutubeMaterialDiscoveryResponse discoveryResponse,
        IReadOnlyList<string> queries)
    {
        var matchedQuery = queries.FirstOrDefault() ?? string.Empty;

        return discoveryResponse.Candidates
            .OrderByDescending(candidate => candidate.RelevanceScore + candidate.AuthorityScore)
            .Take(12)
            .Select(candidate => new Material
            {
                Id = Guid.NewGuid(),
                CourseId = courseId,
                Title = candidate.Title,
                Description = candidate.IsPlaylist
                    ? "Playlist complementar curada para aprofundamento."
                    : "Video complementar curado para aprofundamento.",
                Url = candidate.IsPlaylist
                    ? $"https://www.youtube.com/playlist?list={candidate.VideoId}"
                    : $"https://www.youtube.com/watch?v={candidate.VideoId}",
                ThumbnailUrl = candidate.IsPlaylist
                    ? string.Empty
                    : $"https://i.ytimg.com/vi/{candidate.VideoId}/hqdefault.jpg",
                ChannelName = candidate.ChannelName,
                Source = "YouTube",
                Type = candidate.IsPlaylist ? "Playlist" : "Video",
                VideoId = candidate.VideoId,
                PlaylistId = candidate.IsPlaylist ? candidate.VideoId : string.Empty,
                MatchedQuery = matchedQuery,
                AuthorityScore = candidate.AuthorityScore,
                RelevanceScore = candidate.RelevanceScore
            })
            .ToList();
    }

    private static List<string> ExtractRoadmapHighlights(IReadOnlyList<RoadmapLevel> roadmapLevels)
    {
        return roadmapLevels
            .OrderBy(level => level.Order)
            .SelectMany(level => new[]
            {
                level.Title,
                level.Objective
            }.Concat(level.Stages
                .OrderBy(stage => stage.Order)
                .Select(stage => $"{stage.Title}: {stage.MasteryExpectation}")))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(24)
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

    private static async Task SaveMaterialsInternalAsync(StudyHubDbContext context, Guid courseId, List<Material> materials)
    {
        var record = await context.CourseMaterials.FirstOrDefaultAsync(item => item.CourseId == courseId);

        if (record == null)
        {
            record = new CourseMaterialsRecord { CourseId = courseId };
            await context.CourseMaterials.AddAsync(record);
        }

        record.MaterialsJson = PersistenceMapper.SerializeMaterials(materials);
        record.UpdatedAt = DateTime.Now;

        await context.SaveChangesAsync();
    }

    private sealed record SupplementaryGenerationContext(
        CourseSupplementaryMaterialsRequestContract AiRequest,
        YoutubeMaterialDiscoveryRequest DiscoveryRequest);
}
