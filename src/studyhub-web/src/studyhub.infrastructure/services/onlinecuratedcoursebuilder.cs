using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;

namespace studyhub.infrastructure.services;

public class OnlineCuratedCourseBuilder : IOnlineCuratedCourseBuilder
{
    public Task<OnlineCuratedCourseBlueprint> BuildBlueprintAsync(OnlineCuratedCourseBuildRequest request, CancellationToken cancellationToken = default)
    {
        var provider = string.IsNullOrWhiteSpace(request.PreferredProvider)
            ? "YouTube"
            : request.PreferredProvider.Trim();

        var theme = request.Theme.Trim();
        var objective = request.Objective.Trim();

        var queries = request.SeedQueries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(query => query.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (queries.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(theme))
            {
                queries.Add(theme);
            }

            if (!string.IsNullOrWhiteSpace(theme) && !string.IsNullOrWhiteSpace(objective))
            {
                queries.Add($"{theme} {objective}");
            }
        }

        return Task.FromResult(new OnlineCuratedCourseBlueprint
        {
            Theme = theme,
            Objective = objective,
            Provider = provider,
            DiscoveryQueries = queries,
            AssemblySteps =
            [
                new()
                {
                    Order = 1,
                    Name = "DiscoverCandidates",
                    Description = "Encontrar canais, playlists e videos gratuitos aderentes ao tema e ao objetivo do curso."
                },
                new()
                {
                    Order = 2,
                    Name = "FilterAndScoreSources",
                    Description = "Aplicar aderencia ao tema, relevancia do canal, autoridade, playlists uteis e potencial de complementaridade entre fontes."
                },
                new()
                {
                    Order = 3,
                    Name = "BreakPlaylistsIntoLessons",
                    Description = "Quebrar playlists em aulas individuais para montar modulos, topicos e licoes coesas dentro do app."
                },
                new()
                {
                    Order = 4,
                    Name = "AssembleLearningProgression",
                    Description = "Ordenar a grade final por progressao logica de aprendizagem, priorizando base antes de aprofundamento."
                },
                new()
                {
                    Order = 5,
                    Name = "PersistAsCourse",
                    Description = "Transformar a curadoria aprovada em um curso padronizado do StudyHub com progresso, continuidade, roadmap e materiais associados."
                }
            ],
            SourceMetadataTemplate = new CourseSourceMetadata
            {
                Provider = provider,
                RequestedTopic = theme,
                RequestedObjective = objective,
                SearchQueries = queries
            }
        });
    }

    public Task<OnlineCuratedCourseBuildResult> BuildCourseAsync(OnlineCourseAssemblyRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var courseId = request.Intent.CourseId == Guid.Empty
            ? Guid.NewGuid()
            : request.Intent.CourseId;

        var sourceReferences = new List<CuratedCourseSourceReference>();
        var modules = new List<Module>();
        var selectedSources = request.Curation.SelectedSources
            .ToDictionary(source => $"{source.SourceKind}:{source.SourceId}", StringComparer.OrdinalIgnoreCase);

        foreach (var selection in request.Curation.ModuleSelections.OrderBy(item => item.Order))
        {
            var moduleId = CourseIdentityHelper.CreateModuleId(courseId, selection.Order);
            var topicId = CourseIdentityHelper.CreateTopicId(courseId, selection.Order);
            var refinedModule = request.TextRefinement?.Modules.FirstOrDefault(item => item.ModuleId == moduleId);
            var lessons = new List<Lesson>();
            var seenLessonKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lessonOrder = 1;

            foreach (var source in selection.Sources)
            {
                var sourceKey = $"{source.SourceKind}:{source.SourceId}";
                if (selectedSources.TryGetValue(sourceKey, out var selectedSource))
                {
                    sourceReferences.Add(new CuratedCourseSourceReference
                    {
                        Provider = "YouTube",
                        SourceKind = source.SourceKind,
                        Title = selectedSource.Title,
                        Url = selectedSource.Url,
                        ChannelTitle = selectedSource.ChannelTitle,
                        ChannelUrl = string.Empty,
                        PlaylistId = selectedSource.PlaylistId,
                        VideoId = selectedSource.VideoId,
                        ChannelAudienceSize = selectedSource.SubscriberCount,
                        AuthorityScore = selectedSource.AuthorityScore,
                        RelevanceScore = selectedSource.RelevanceScore
                    });
                }

                var lessonSeeds = source.Lessons.Count > 0
                    ? source.Lessons
                    : [new YouTubeVideoDescriptor
                    {
                        VideoId = source.SourceId,
                        Title = source.Title,
                        Url = source.Url,
                        ChannelTitle = source.ChannelTitle
                    }];

                foreach (var lessonSeed in lessonSeeds)
                {
                    var sourceLessonKey = !string.IsNullOrWhiteSpace(lessonSeed.VideoId)
                        ? lessonSeed.VideoId
                        : lessonSeed.Url;

                    if (!seenLessonKeys.Add(sourceLessonKey))
                    {
                        continue;
                    }

                    var lessonId = CourseIdentityHelper.CreateLessonId(courseId, selection.Order, lessonOrder, sourceLessonKey);
                    var refinedLesson = refinedModule?.Lessons.FirstOrDefault(item => item.LessonId == lessonId);

                    lessons.Add(new Lesson
                    {
                        Id = lessonId,
                        TopicId = topicId,
                        Order = lessonOrder++,
                        RawTitle = FirstNonEmpty(lessonSeed.Title, source.Title, $"Aula {lessonOrder - 1}"),
                        RawDescription = FirstNonEmpty(lessonSeed.Description, source.Title),
                        Title = FirstNonEmpty(refinedLesson?.Title, lessonSeed.Title, source.Title, $"Aula {lessonOrder - 1}"),
                        Description = FirstNonEmpty(refinedLesson?.Description, lessonSeed.Description, source.Title),
                        SourceType = LessonSourceType.ExternalVideo,
                        ExternalUrl = FirstNonEmpty(lessonSeed.Url, source.Url),
                        Provider = "YouTube",
                        Duration = lessonSeed.Duration
                    });
                }
            }

            if (lessons.Count == 0)
            {
                continue;
            }

            modules.Add(new Module
            {
                Id = moduleId,
                CourseId = courseId,
                Order = selection.Order,
                RawTitle = FirstNonEmpty(selection.ModuleTitle, $"Modulo {selection.Order}"),
                RawDescription = FirstNonEmpty(
                    request.Planning.Modules.FirstOrDefault(item => item.Order == selection.Order)?.Description,
                    selection.ModuleObjective),
                Title = FirstNonEmpty(refinedModule?.Title, selection.ModuleTitle, $"Modulo {selection.Order}"),
                Description = FirstNonEmpty(
                    refinedModule?.Description,
                    request.Planning.Modules.FirstOrDefault(item => item.Order == selection.Order)?.Description,
                    selection.ModuleObjective),
                Topics =
                [
                    new Topic
                    {
                        Id = topicId,
                        ModuleId = moduleId,
                        Order = 1,
                        RawTitle = FirstNonEmpty(selection.ModuleTitle, $"Trilha {selection.Order}"),
                        RawDescription = FirstNonEmpty(selection.ModuleObjective, refinedModule?.Description),
                        Title = FirstNonEmpty(selection.ModuleTitle, $"Trilha {selection.Order}"),
                        Description = FirstNonEmpty(selection.ModuleObjective, refinedModule?.Description),
                        Lessons = lessons
                    }
                ]
            });
        }

        if (modules.Count == 0)
        {
            throw new InvalidOperationException("Nao foi possivel montar uma estrutura de curso online com as fontes curadas selecionadas.");
        }

        var normalizedSourceReferences = sourceReferences
            .GroupBy(item => $"{item.SourceKind}:{item.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var searchQueries = request.Curation.SelectedQueries.Count > 0
            ? request.Curation.SelectedQueries
            : request.Planning.DiscoveryQueries;

        var course = new Course
        {
            Id = courseId,
            RawTitle = FirstNonEmpty(request.Planning.FriendlyTitle, request.Intent.Topic),
            RawDescription = FirstNonEmpty(request.Planning.CourseDescription, request.Intent.Objective),
            Title = FirstNonEmpty(request.TextRefinement?.RefinedCourseTitle, request.Planning.FriendlyTitle, request.Intent.Topic),
            Description = FirstNonEmpty(request.TextRefinement?.RefinedCourseDescription, request.Planning.CourseDescription, request.Intent.Objective),
            Category = "Curadoria Online",
            ThumbnailUrl = string.Empty,
            SourceType = CourseSourceType.OnlineCurated,
            SourceMetadata = new CourseSourceMetadata
            {
                ImportedAt = DateTime.UtcNow,
                ScanVersion = "online-curated-v1",
                Provider = "YouTube",
                RequestedTopic = request.Intent.Topic,
                RequestedObjective = request.Intent.Objective,
                SearchQueries = searchQueries.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SourceUrls = normalizedSourceReferences.Select(item => item.Url).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                PlaylistIds = normalizedSourceReferences.Select(item => item.PlaylistId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                VideoIds = normalizedSourceReferences.Select(item => item.VideoId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                CuratedSources = normalizedSourceReferences,
                CompletedSteps =
                [
                    "IntentCaptured",
                    "PedagogicalPlanningGenerated",
                    "YouTubeDiscoveryCompleted",
                    "SourceCurationCompleted",
                    "OnlineCourseBuilt"
                ],
                GenerationSummary = request.Curation.Summary
            },
            TotalDuration = TimeSpan.FromTicks(modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Sum(lesson => lesson.Duration.Ticks)),
            AddedAt = DateTime.Now,
            Modules = modules
        };

        return Task.FromResult(new OnlineCuratedCourseBuildResult
        {
            Course = course,
            CuratedSources = normalizedSourceReferences
        });
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
