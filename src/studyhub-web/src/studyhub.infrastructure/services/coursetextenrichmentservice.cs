using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;
using studyhub.domain.Entities;

namespace studyhub.infrastructure.services;

public sealed class CourseTextEnrichmentService(
    IGeminiCourseProvider geminiCourseProvider,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    ILogger<CourseTextEnrichmentService> logger)
{
    private const int MaxLessonsPerBatch = 30;

    private readonly IGeminiCourseProvider _geminiCourseProvider = geminiCourseProvider;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly ILogger<CourseTextEnrichmentService> _logger = logger;

    public async Task<CourseTextEnrichmentResult> RefineCourseAsync(
        Course course,
        string topLevelStepKey,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var batches = BuildBatches(course, topLevelStepKey);
        var provider = "Gemini";
        var existingStepStates = await _courseGenerationHistoryService.GetStepStatesAsync(course.Id, cancellationToken);

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = course.Id,
            StepKey = topLevelStepKey,
            Provider = provider,
            Status = CourseGenerationStepStatus.Running,
            RequestJson = IntegrationJsonHelper.Serialize(new
            {
                forceRefresh,
                batchCount = batches.Count,
                moduleCount = course.Modules.Count
            })
        }, cancellationToken);

        var result = new CourseTextEnrichmentResult
        {
            CourseId = course.Id,
            TotalBatches = batches.Count
        };

        foreach (var batch in batches)
        {
            if (!forceRefresh &&
                existingStepStates.TryGetValue(batch.StepKey, out var existingStep) &&
                existingStep.Status == CourseGenerationStepStatus.Succeeded)
            {
                result.SkippedBatches++;
                continue;
            }

            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = course.Id,
                StepKey = batch.StepKey,
                Provider = provider,
                Status = CourseGenerationStepStatus.Running,
                RequestJson = IntegrationJsonHelper.Serialize(batch.Request)
            }, cancellationToken);

            try
            {
                var response = await _geminiCourseProvider.RefineCourseTextAsync(batch.Request, cancellationToken);
                var batchUpdated = ApplyBatch(course, batch, response);

                await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
                {
                    CourseId = course.Id,
                    StepKey = batch.StepKey,
                    Provider = provider,
                    Status = CourseGenerationStepStatus.Succeeded,
                    RequestJson = IntegrationJsonHelper.Serialize(batch.Request),
                    ResponseJson = IntegrationJsonHelper.Serialize(response)
                }, cancellationToken);

                result.ProcessedBatches++;
                if (batchUpdated)
                {
                    result.Updated = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chunked text enrichment failed for course {CourseId}, batch {StepKey}.", course.Id, batch.StepKey);
                await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
                {
                    CourseId = course.Id,
                    StepKey = batch.StepKey,
                    Provider = provider,
                    Status = CourseGenerationStepStatus.Failed,
                    RequestJson = IntegrationJsonHelper.Serialize(batch.Request),
                    ErrorMessage = ex.Message
                }, cancellationToken);

                result.FailedBatches++;
                result.Errors.Add($"{batch.StepKey}: {ex.Message}");
            }
        }

        await RecordAggregateStepAsync(course.Id, topLevelStepKey, provider, result, cancellationToken);
        return result;
    }

    private async Task RecordAggregateStepAsync(
        Guid courseId,
        string topLevelStepKey,
        string provider,
        CourseTextEnrichmentResult result,
        CancellationToken cancellationToken)
    {
        if (result.TotalBatches == 0 || (result.ProcessedBatches == 0 && result.FailedBatches == 0 && result.SkippedBatches > 0))
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = topLevelStepKey,
                Provider = provider,
                Status = CourseGenerationStepStatus.Skipped,
                ErrorMessage = "Todos os lotes de refinamento ja estavam concluidos para este curso.",
                ResponseJson = IntegrationJsonHelper.Serialize(new
                {
                    result.TotalBatches,
                    result.ProcessedBatches,
                    result.SkippedBatches,
                    result.FailedBatches
                })
            }, cancellationToken);
            return;
        }

        if (result.FailedBatches == 0)
        {
            await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
            {
                CourseId = courseId,
                StepKey = topLevelStepKey,
                Provider = provider,
                Status = CourseGenerationStepStatus.Succeeded,
                ResponseJson = IntegrationJsonHelper.Serialize(new
                {
                    result.TotalBatches,
                    result.ProcessedBatches,
                    result.SkippedBatches,
                    result.FailedBatches,
                    result.Updated
                })
            }, cancellationToken);
            return;
        }

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = topLevelStepKey,
            Provider = provider,
            Status = CourseGenerationStepStatus.Failed,
            ErrorMessage = $"Falha parcial no refinamento textual: {result.FailedBatches} lote(s) com erro.",
            ResponseJson = IntegrationJsonHelper.Serialize(new
            {
                result.TotalBatches,
                result.ProcessedBatches,
                result.SkippedBatches,
                result.FailedBatches,
                result.Errors
            })
        }, cancellationToken);
    }

    private static List<CourseTextBatch> BuildBatches(Course course, string topLevelStepKey)
    {
        var batches = new List<CourseTextBatch>();

        foreach (var module in course.Modules.OrderBy(module => module.Order))
        {
            var orderedLessons = module.Topics
                .OrderBy(topic => topic.Order)
                .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
                .ToList();

            if (orderedLessons.Count == 0)
            {
                batches.Add(BuildBatch(course, module, [], 1, topLevelStepKey));
                continue;
            }

            var batchIndex = 1;
            foreach (var lessonChunk in ChunkLessons(orderedLessons, MaxLessonsPerBatch))
            {
                batches.Add(BuildBatch(course, module, lessonChunk, batchIndex++, topLevelStepKey));
            }
        }

        return batches;
    }

    private static IEnumerable<IReadOnlyList<Lesson>> ChunkLessons(IReadOnlyList<Lesson> lessons, int batchSize)
    {
        for (var index = 0; index < lessons.Count; index += batchSize)
        {
            yield return lessons.Skip(index).Take(batchSize).ToList();
        }
    }

    private static CourseTextBatch BuildBatch(Course course, Module module, IReadOnlyList<Lesson> lessons, int batchIndex, string topLevelStepKey)
    {
        var request = new CourseTextRefinementRequest
        {
            CourseId = course.Id,
            SourceType = course.SourceType.ToString(),
            CourseTitle = FirstNonEmpty(course.RawTitle, course.Title),
            CourseDescription = FirstNonEmpty(course.RawDescription, course.Description),
            Modules =
            [
                new ModuleTextRefinementInput
                {
                    ModuleId = module.Id,
                    Title = FirstNonEmpty(module.RawTitle, module.Title),
                    Description = FirstNonEmpty(module.RawDescription, module.Description),
                    Lessons = lessons
                        .Select(lesson => new LessonTextRefinementInput
                        {
                            LessonId = lesson.Id,
                            Title = FirstNonEmpty(lesson.RawTitle, lesson.Title),
                            Description = FirstNonEmpty(lesson.RawDescription, lesson.Description)
                        })
                        .ToList()
                }
            ]
        };

        var signatureBuilder = new StringBuilder();
        signatureBuilder.Append(module.Id.ToString("N"));
        signatureBuilder.Append('|');
        signatureBuilder.Append(FirstNonEmpty(module.RawTitle, module.Title));
        foreach (var lesson in lessons)
        {
            signatureBuilder.Append('|');
            signatureBuilder.Append(lesson.Id.ToString("N"));
            signatureBuilder.Append(':');
            signatureBuilder.Append(FirstNonEmpty(lesson.RawTitle, lesson.Title));
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signatureBuilder.ToString())))
            .Substring(0, 12)
            .ToLowerInvariant();

        return new CourseTextBatch(
            StepKey: $"{topLevelStepKey}:module:{module.Id:N}:batch:{batchIndex:D2}:{hash}",
            Request: request,
            ModuleId: module.Id,
            LessonIds: lessons.Select(lesson => lesson.Id).ToHashSet());
    }

    private static bool ApplyBatch(Course course, CourseTextBatch batch, CourseTextRefinementResponse response)
    {
        var updated = false;
        var module = course.Modules.FirstOrDefault(item => item.Id == batch.ModuleId);
        if (module == null)
        {
            return false;
        }

        var refinedModule = response.Modules.FirstOrDefault(item => item.ModuleId == batch.ModuleId);
        if (refinedModule == null)
        {
            return false;
        }

        updated |= ApplyIfChanged(module.Title, FirstNonEmpty(refinedModule.Title, module.Title), value => module.Title = value);
        updated |= ApplyIfChanged(module.Description, FirstNonEmpty(refinedModule.Description, module.Description), value => module.Description = value);

        foreach (var lesson in module.Topics
                     .SelectMany(topic => topic.Lessons)
                     .Where(lesson => batch.LessonIds.Contains(lesson.Id)))
        {
            var refinedLesson = refinedModule.Lessons.FirstOrDefault(item => item.LessonId == lesson.Id);
            if (refinedLesson == null)
            {
                continue;
            }

            updated |= ApplyIfChanged(lesson.Title, FirstNonEmpty(refinedLesson.Title, lesson.Title), value => lesson.Title = value);
            updated |= ApplyIfChanged(lesson.Description, FirstNonEmpty(refinedLesson.Description, lesson.Description), value => lesson.Description = value);
        }

        return updated;
    }

    private static bool ApplyIfChanged(string currentValue, string candidate, Action<string> assign)
    {
        if (string.Equals(currentValue, candidate, StringComparison.Ordinal))
        {
            return false;
        }

        assign(candidate);
        return true;
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

    private sealed record CourseTextBatch(
        string StepKey,
        CourseTextRefinementRequest Request,
        Guid ModuleId,
        HashSet<Guid> LessonIds);
}

public sealed class CourseTextEnrichmentResult
{
    public Guid CourseId { get; set; }
    public int TotalBatches { get; set; }
    public int ProcessedBatches { get; set; }
    public int SkippedBatches { get; set; }
    public int FailedBatches { get; set; }
    public bool Updated { get; set; }
    public List<string> Errors { get; set; } = [];
}
