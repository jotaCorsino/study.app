using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;

namespace studyhub.infrastructure.services;

public class LocalFolderCourseBuilder(IVideoMetadataReader videoMetadataReader) : ILocalFolderCourseBuilder
{
    private const string LocalScanVersion = "local-folder-v1";
    private readonly LocalCourseScanner _scanner = new(videoMetadataReader);

    public async Task<LocalFolderCourseBuildResult> BuildAsync(LocalFolderCourseBuildRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedFolderPath = Path.GetFullPath(request.FolderPath);
        var detectedStructure = await _scanner.ScanAsync(normalizedFolderPath, cancellationToken);

        return new LocalFolderCourseBuildResult
        {
            DetectedStructure = detectedStructure,
            Course = BuildCourse(detectedStructure, request)
        };
    }

    private static Course BuildCourse(DetectedCourseStructure detectedStructure, LocalFolderCourseBuildRequest request)
    {
        var modules = new List<Module>();

        foreach (var detectedModule in detectedStructure.Modules.OrderBy(module => module.Order))
        {
            var moduleRawTitle = detectedModule.RawName;
            var moduleTitle = LocalCourseScanner.NormalizeDisplayName(detectedModule.RawName);
            var topics = new List<Topic>();

            foreach (var detectedTopic in detectedModule.Topics.OrderBy(topic => topic.Order))
            {
                var topicRawTitle = detectedTopic.RawName;
                var topicTitle = detectedTopic.RelativePath == "."
                    ? moduleTitle
                    : LocalCourseScanner.NormalizeDisplayName(detectedTopic.RawName);

                var lessons = new List<Lesson>();

                foreach (var detectedLesson in detectedTopic.Lessons.OrderBy(lesson => lesson.Order))
                {
                    var lesson = new Lesson
                    {
                        Id = detectedLesson.LessonId,
                        TopicId = detectedTopic.TopicId,
                        Order = detectedLesson.Order,
                        RawTitle = detectedLesson.RawName,
                        RawDescription = string.Empty,
                        Title = LocalCourseScanner.NormalizeDisplayName(detectedLesson.RawName, stripExtension: true),
                        Description = string.Empty,
                        SourceType = LessonSourceType.LocalFile,
                        LocalFilePath = detectedLesson.AbsolutePath,
                        Provider = "LocalFileSystem",
                        Duration = detectedLesson.Duration
                    };

                    if (request.ExistingLessonStates.TryGetValue(lesson.Id, out var state))
                    {
                        lesson.Status = state.Status;
                        lesson.WatchedPercentage = state.WatchedPercentage;
                        lesson.LastPlaybackPosition = TimeSpan.FromSeconds(state.LastPlaybackPositionSeconds);

                        if (lesson.Duration == TimeSpan.Zero && state.DurationMinutes > 0)
                        {
                            lesson.Duration = TimeSpan.FromMinutes(state.DurationMinutes);
                        }
                    }

                    lessons.Add(lesson);
                }

                topics.Add(new Topic
                {
                    Id = detectedTopic.TopicId,
                    ModuleId = detectedModule.ModuleId,
                    Order = detectedTopic.Order,
                    RawTitle = topicRawTitle,
                    RawDescription = string.Empty,
                    Title = topicTitle,
                    Description = string.Empty,
                    Lessons = lessons
                });
            }

            modules.Add(new Module
            {
                Id = detectedModule.ModuleId,
                CourseId = detectedStructure.CourseId,
                Order = detectedModule.Order,
                RawTitle = moduleRawTitle,
                RawDescription = string.Empty,
                Title = moduleTitle,
                Description = string.Empty,
                Topics = topics
            });
        }

        return new Course
        {
            Id = detectedStructure.CourseId,
            RawTitle = detectedStructure.RootFolderName,
            RawDescription = $"Curso importado automaticamente da pasta local \"{detectedStructure.RootFolderName}\".",
            Title = LocalCourseScanner.NormalizeDisplayName(detectedStructure.RootFolderName),
            Description = $"Curso importado automaticamente da pasta local \"{detectedStructure.RootFolderName}\".",
            Category = "Curso Local",
            ThumbnailUrl = string.Empty,
            SourceType = CourseSourceType.LocalFolder,
            SourceMetadata = new CourseSourceMetadata
            {
                RootPath = detectedStructure.RootFolderPath,
                ImportedAt = detectedStructure.ScannedAt,
                ScanVersion = LocalScanVersion,
                Provider = "LocalFileSystem",
                CompletedSteps =
                [
                    "LocalStructureImported"
                ]
            },
            TotalDuration = TimeSpan.FromTicks(modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Sum(lesson => lesson.Duration.Ticks)),
            AddedAt = request.ExistingAddedAt ?? DateTime.Now,
            LastAccessedAt = request.ExistingLastAccessedAt,
            Modules = modules
        };
    }
}
