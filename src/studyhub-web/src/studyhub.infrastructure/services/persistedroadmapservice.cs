using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;
using studyhub.domain.AIContracts;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public sealed class PersistedRoadmapService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    ICourseService courseService,
    IIntegrationSettingsService integrationSettingsService,
    IGeminiCourseProvider geminiCourseProvider,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    ILogger<PersistedRoadmapService> logger) : IRoadmapService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ICourseService _courseService = courseService;
    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly ILogger<PersistedRoadmapService> _logger = logger;

    public async Task<List<RoadmapLevel>> GetRoadmapByCourseAsync(Guid courseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await context.CourseRoadmaps
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == courseId);

        return record == null
            ? []
            : NormalizeCourseId(courseId, PersistenceMapper.DeserializeRoadmap(record.LevelsJson));
    }

    public async Task GenerateRoadmapAsync(Guid courseId)
    {
        var settings = await _integrationSettingsService.GetSettingsAsync();
        if (!settings.HasGeminiKey)
        {
            await RecordSkippedAsync(courseId, "Gemini API key ausente.");
            return;
        }

        var course = await _courseService.GetCourseByIdAsync(courseId);
        if (course == null)
        {
            return;
        }

        var request = BuildRoadmapRequest(course);

        try
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.RoadmapGeneration,
                Provider = "Gemini",
                Status = CourseGenerationStepStatus.Running,
                RequestJson = IntegrationJsonHelper.Serialize(request)
            });

            var response = await _geminiCourseProvider.GenerateRoadmapAsync(request);
            var levels = MapRoadmapLevels(courseId, response);

            await SaveRoadmapAsync(courseId, levels);
            await MarkRoadmapGeneratedAsync(courseId);

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.RoadmapGeneration,
                Provider = "Gemini",
                Status = CourseGenerationStepStatus.Succeeded,
                RequestJson = IntegrationJsonHelper.Serialize(request),
                ResponseJson = IntegrationJsonHelper.Serialize(response)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Roadmap generation failed for course {CourseId}.", courseId);
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = OnlineCourseStepKeys.RoadmapGeneration,
                Provider = "Gemini",
                Status = CourseGenerationStepStatus.Failed,
                RequestJson = IntegrationJsonHelper.Serialize(request),
                ErrorMessage = ex.Message
            });
        }
    }

    public async Task SaveRoadmapAsync(Guid courseId, List<RoadmapLevel> roadmapLevels)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await SaveRoadmapInternalAsync(context, courseId, roadmapLevels);
    }

    private async Task RecordSkippedAsync(Guid courseId, string reason)
    {
        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.RoadmapGeneration,
            Provider = "Gemini",
            Status = CourseGenerationStepStatus.Skipped,
            ErrorMessage = reason
        });
    }

    private async Task MarkRoadmapGeneratedAsync(Guid courseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var courseRecord = await context.Courses.FirstOrDefaultAsync(item => item.Id == courseId);
        if (courseRecord == null)
        {
            return;
        }

        var metadata = string.IsNullOrWhiteSpace(courseRecord.SourceMetadataJson)
            ? new CourseSourceMetadata()
            : System.Text.Json.JsonSerializer.Deserialize<CourseSourceMetadata>(courseRecord.SourceMetadataJson, IntegrationJsonHelper.JsonOptions) ?? new CourseSourceMetadata();

        metadata.CompletedSteps = metadata.CompletedSteps
            .Concat(["RoadmapGenerated"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        metadata.LastEnrichedAt = DateTime.UtcNow;

        courseRecord.SourceMetadataJson = PersistenceMapper.SerializeCourseSourceMetadata(metadata);
        await context.SaveChangesAsync();
    }

    private static async Task SaveRoadmapInternalAsync(StudyHubDbContext context, Guid courseId, List<RoadmapLevel> roadmapLevels)
    {
        var normalizedLevels = NormalizeCourseId(courseId, roadmapLevels);
        var record = await context.CourseRoadmaps.FirstOrDefaultAsync(item => item.CourseId == courseId);

        if (record == null)
        {
            record = new CourseRoadmapRecord { CourseId = courseId };
            await context.CourseRoadmaps.AddAsync(record);
        }

        record.LevelsJson = PersistenceMapper.SerializeRoadmap(normalizedLevels);
        record.UpdatedAt = DateTime.Now;

        await context.SaveChangesAsync();
    }

    private static CourseRoadmapRequestContract BuildRoadmapRequest(Course course)
    {
        var goal = course.SourceType switch
        {
            CourseSourceType.OnlineCurated => "Gerar um roadmap coerente para um curso online curado a partir de videos gratuitos.",
            CourseSourceType.ExternalImport => "Gerar um roadmap coerente para um curso externo importado via JSON, preservando a estrutura academica original ja persistida.",
            _ => "Gerar um roadmap coerente para um curso local importado do disco sem trocar sua estrutura principal."
        };

        return new CourseRoadmapRequestContract
        {
            Goal = goal,
            CourseInformation = new RoadmapCourseInformationContract
            {
                Title = course.Title,
                Description = course.Description,
                DurationMinutes = (int)Math.Round(course.TotalDuration.TotalMinutes),
                ModuleCount = course.Modules.Count,
                LessonCount = course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count(),
                Modules = course.Modules
                    .OrderBy(module => module.Order)
                    .Select(module => new RoadmapModuleContract
                    {
                        Title = module.Title,
                        TopicCount = module.Topics.Count,
                        LessonCount = module.Topics.SelectMany(topic => topic.Lessons).Count()
                    })
                    .ToList()
            }
        };
    }

    private static List<RoadmapLevel> MapRoadmapLevels(Guid courseId, CourseRoadmapResponseContract response)
    {
        return response.Levels
            .OrderBy(level => level.Order)
            .Select(level => new RoadmapLevel
            {
                CourseId = courseId,
                Order = level.Order,
                Kicker = level.Kicker,
                Title = level.Title,
                Objective = level.Objective,
                DetailedGoal = level.DetailedGoal,
                FocusTags = level.FocusTags,
                Stages = level.Stages
                    .OrderBy(stage => stage.Order)
                    .Select(stage => new RoadmapStage
                    {
                        Order = stage.Order,
                        Kicker = stage.Kicker,
                        Title = stage.Title,
                        Subtitle = stage.Subtitle,
                        Blocks = stage.Blocks.Select(block => new RoadmapBlock
                        {
                            Title = block.Title,
                            Description = block.Description,
                            Items = block.Items.Select(item => new RoadmapChecklistItem
                            {
                                Description = item.Description
                            }).ToList()
                        }).ToList(),
                        MasteryExpectation = stage.MasteryExpectation,
                        CommonMistakes = stage.CommonMistakes,
                        ValidationQuestions = stage.ValidationQuestions
                    })
                    .ToList()
            })
            .ToList();
    }

    private static List<RoadmapLevel> NormalizeCourseId(Guid courseId, List<RoadmapLevel> roadmapLevels)
    {
        foreach (var level in roadmapLevels)
        {
            level.CourseId = courseId;
        }

        return roadmapLevels;
    }
}
