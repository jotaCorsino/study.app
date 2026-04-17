using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public class PersistedCourseService(IDbContextFactory<StudyHubDbContext> contextFactory) : ICourseService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;

    public async Task<List<Course>> GetAllCoursesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var records = await BuildCourseQuery(context)
            .OrderBy(record => record.AddedAt)
            .ToListAsync();

        return records.Select(record => record.ToDomain()).ToList();
    }

    public async Task<Course?> GetCourseByIdAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await BuildCourseQuery(context)
            .FirstOrDefaultAsync(course => course.Id == id);

        if (record == null)
        {
            return null;
        }

        if (record.SourceType != CourseSourceType.LocalFolder)
        {
            return record.ToDomain();
        }

        var manifest = await LoadOrCreateLocalManifestAsync(context, record);
        if (manifest == null || !NeedsLocalRehydration(record, manifest))
        {
            return record.ToDomain();
        }

        var rehydratedCourse = await RehydrateLocalCourseFromManifestAsync(id, manifest);
        return rehydratedCourse ?? record.ToDomain();
    }

    public async Task<CourseSourceMetadata?> UpdateCourseIntroSkipPreferenceAsync(Guid id, bool introSkipEnabled, int introSkipSeconds)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await context.Courses.FirstOrDefaultAsync(course => course.Id == id);
        if (record == null)
        {
            return null;
        }

        var metadata = DeserializeCourseSourceMetadata(record);
        metadata.IntroSkipEnabled = introSkipEnabled;
        metadata.IntroSkipSeconds = introSkipSeconds;

        if (record.SourceType == CourseSourceType.LocalFolder &&
            string.IsNullOrWhiteSpace(metadata.RootPath))
        {
            metadata.RootPath = record.FolderPath;
        }

        record.SourceMetadataJson = PersistenceMapper.SerializeCourseSourceMetadata(metadata);
        await context.SaveChangesAsync();

        return metadata;
    }

    public async Task<Lesson?> GetLessonByIdAsync(Guid courseId, Guid lessonId)
    {
        var course = await GetCourseByIdAsync(courseId);

        return course?.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .FirstOrDefault(lesson => lesson.Id == lessonId);
    }

    public async Task<Lesson?> GetNextLessonAsync(Guid courseId, Guid currentLessonId)
    {
        var lessons = await GetOrderedLessonsAsync(courseId);
        var index = lessons.FindIndex(lesson => lesson.Id == currentLessonId);

        return index >= 0 && index < lessons.Count - 1 ? lessons[index + 1] : null;
    }

    public async Task<Lesson?> GetPreviousLessonAsync(Guid courseId, Guid currentLessonId)
    {
        var lessons = await GetOrderedLessonsAsync(courseId);
        var index = lessons.FindIndex(lesson => lesson.Id == currentLessonId);

        return index > 0 ? lessons[index - 1] : null;
    }

    public async Task DeleteCourseAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await context.Courses.FirstOrDefaultAsync(course => course.Id == id);
        if (record == null)
        {
            return;
        }

        context.Courses.Remove(record);
        await context.SaveChangesAsync();
    }

    private async Task<List<Lesson>> GetOrderedLessonsAsync(Guid courseId)
    {
        var course = await GetCourseByIdAsync(courseId);

        return course?.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList() ?? [];
    }

    private static IQueryable<persistence.models.CourseRecord> BuildCourseQuery(StudyHubDbContext context)
    {
        return context.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons);
    }

    private async Task<DetectedCourseStructure?> LoadOrCreateLocalManifestAsync(
        StudyHubDbContext context,
        persistence.models.CourseRecord record)
    {
        var snapshot = await context.CourseImportSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == record.Id);

        var manifest = TryDeserializeManifest(snapshot?.StructureJson);
        if (manifest != null)
        {
            return manifest;
        }

        if (!HasAnyLessons(record))
        {
            return null;
        }

        var legacyManifest = BuildManifestFromCourse(record.ToDomain());
        await UpsertLocalManifestAsync(context, legacyManifest);
        return legacyManifest;
    }

    private async Task<Course?> RehydrateLocalCourseFromManifestAsync(Guid courseId, DetectedCourseStructure manifest)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var trackedCourse = await context.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (trackedCourse == null || trackedCourse.SourceType != CourseSourceType.LocalFolder)
        {
            return null;
        }

        var existingCourse = trackedCourse.ToDomain();
        var preservedLessonState = existingCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToDictionary(
                lesson => lesson.Id,
                lesson => new PreservedLessonState
                {
                    Status = lesson.Status,
                    WatchedPercentage = lesson.WatchedPercentage,
                    LastPlaybackPositionSeconds = Math.Max(0, (int)Math.Round(lesson.LastPlaybackPosition.TotalSeconds)),
                    DurationMinutes = Math.Max(0, (int)Math.Round(lesson.Duration.TotalMinutes))
                });

        var rebuiltCourse = BuildCourseFromManifest(manifest, existingCourse, preservedLessonState);
        if (!HasAnyLessons(rebuiltCourse))
        {
            return null;
        }

        var previousCurrentLessonId = trackedCourse.CurrentLessonId;
        await CoursePersistenceHelper.UpsertCourseAsync(context, rebuiltCourse);

        if (previousCurrentLessonId.HasValue)
        {
            var hasCurrentLesson = await context.Lessons
                .AsNoTracking()
                .AnyAsync(lesson =>
                    lesson.Id == previousCurrentLessonId.Value &&
                    lesson.Topic != null &&
                    lesson.Topic.Module != null &&
                    lesson.Topic.Module.CourseId == courseId);

            if (hasCurrentLesson)
            {
                var persistedCourse = await context.Courses.FirstOrDefaultAsync(course => course.Id == courseId);
                if (persistedCourse != null && persistedCourse.CurrentLessonId != previousCurrentLessonId)
                {
                    persistedCourse.CurrentLessonId = previousCurrentLessonId;
                    await context.SaveChangesAsync();
                }
            }
        }

        var persistedRecord = await BuildCourseQuery(context)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        return persistedRecord?.ToDomain();
    }

    private static Course BuildCourseFromManifest(
        DetectedCourseStructure manifest,
        Course existingCourse,
        IReadOnlyDictionary<Guid, PreservedLessonState> preservedLessonState)
    {
        var modules = new List<Module>();

        foreach (var detectedModule in manifest.Modules.OrderBy(module => module.Order))
        {
            var moduleRawTitle = FirstNonEmpty(detectedModule.RawName, $"Modulo {detectedModule.Order}");
            var moduleTitle = LocalCourseScanner.NormalizeDisplayName(moduleRawTitle);
            var topics = new List<Topic>();

            foreach (var detectedTopic in detectedModule.Topics.OrderBy(topic => topic.Order))
            {
                var topicRawTitle = FirstNonEmpty(detectedTopic.RawName, moduleRawTitle);
                var topicTitle = string.Equals(detectedTopic.RelativePath, ".", StringComparison.Ordinal)
                    ? moduleTitle
                    : LocalCourseScanner.NormalizeDisplayName(topicRawTitle);
                var lessons = new List<Lesson>();

                foreach (var detectedLesson in detectedTopic.Lessons.OrderBy(lesson => lesson.Order))
                {
                    var lessonRawTitle = FirstNonEmpty(
                        detectedLesson.RawName,
                        Path.GetFileNameWithoutExtension(detectedLesson.FileName),
                        $"Aula {detectedLesson.Order}");
                    var absolutePath = ResolveAbsolutePath(detectedLesson, manifest.RootFolderPath);
                    var duration = detectedLesson.Duration > TimeSpan.Zero
                        ? detectedLesson.Duration
                        : TimeSpan.Zero;

                    if (preservedLessonState.TryGetValue(detectedLesson.LessonId, out var savedState))
                    {
                        if (duration == TimeSpan.Zero && savedState.DurationMinutes > 0)
                        {
                            duration = TimeSpan.FromMinutes(savedState.DurationMinutes);
                        }
                    }

                    var lesson = new Lesson
                    {
                        Id = detectedLesson.LessonId,
                        TopicId = detectedTopic.TopicId,
                        Order = detectedLesson.Order,
                        RawTitle = lessonRawTitle,
                        RawDescription = string.Empty,
                        Title = LocalCourseScanner.NormalizeDisplayName(lessonRawTitle, stripExtension: true),
                        Description = string.Empty,
                        SourceType = LessonSourceType.LocalFile,
                        LocalFilePath = absolutePath,
                        Provider = "LocalFileSystem",
                        Duration = duration
                    };

                    if (preservedLessonState.TryGetValue(lesson.Id, out savedState))
                    {
                        lesson.Status = savedState.Status;
                        lesson.WatchedPercentage = savedState.WatchedPercentage;
                        lesson.LastPlaybackPosition = TimeSpan.FromSeconds(savedState.LastPlaybackPositionSeconds);
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
                CourseId = manifest.CourseId,
                Order = detectedModule.Order,
                RawTitle = moduleRawTitle,
                RawDescription = string.Empty,
                Title = moduleTitle,
                Description = string.Empty,
                Topics = topics
            });
        }

        var rebuiltCourse = new Course
        {
            Id = manifest.CourseId,
            RawTitle = FirstNonEmpty(manifest.RootFolderName, existingCourse.RawTitle, existingCourse.Title),
            RawDescription = FirstNonEmpty(
                existingCourse.RawDescription,
                $"Curso importado automaticamente da pasta local \"{manifest.RootFolderName}\"."),
            Title = FirstNonEmpty(existingCourse.Title, LocalCourseScanner.NormalizeDisplayName(manifest.RootFolderName)),
            Description = FirstNonEmpty(existingCourse.Description, existingCourse.RawDescription),
            Category = FirstNonEmpty(existingCourse.Category, "Curso Local"),
            ThumbnailUrl = existingCourse.ThumbnailUrl,
            SourceType = CourseSourceType.LocalFolder,
            SourceMetadata = BuildLocalMetadata(manifest, existingCourse.SourceMetadata),
            TotalDuration = TimeSpan.FromTicks(modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Sum(lesson => lesson.Duration.Ticks)),
            AddedAt = existingCourse.AddedAt == default ? DateTime.Now : existingCourse.AddedAt,
            LastAccessedAt = existingCourse.LastAccessedAt,
            Modules = modules
        };

        CoursePresentationMergeHelper.MergeExistingPresentation(rebuiltCourse, existingCourse);
        rebuiltCourse.SourceMetadata.CompletedSteps = rebuiltCourse.SourceMetadata.CompletedSteps
            .Concat(["LocalStructureRehydratedFromManifest"])
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return rebuiltCourse;
    }

    private static CourseSourceMetadata BuildLocalMetadata(DetectedCourseStructure manifest, CourseSourceMetadata existingMetadata)
    {
        var metadata = existingMetadata ?? new CourseSourceMetadata();

        metadata.RootPath = FirstNonEmpty(manifest.RootFolderPath, metadata.RootPath);
        metadata.ImportedAt ??= manifest.ScannedAt == default ? DateTime.UtcNow : manifest.ScannedAt;
        metadata.ScanVersion = FirstNonEmpty(metadata.ScanVersion, "local-folder-manifest");
        metadata.Provider = FirstNonEmpty(metadata.Provider, "LocalFileSystem");
        metadata.CompletedSteps = metadata.CompletedSteps.Count == 0
            ? ["LocalStructureImported"]
            : metadata.CompletedSteps;

        return metadata;
    }

    private static bool NeedsLocalRehydration(persistence.models.CourseRecord record, DetectedCourseStructure manifest)
    {
        var manifestModuleIds = manifest.Modules.Select(module => module.ModuleId).ToHashSet();
        var manifestLessonIds = manifest.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Select(lesson => lesson.LessonId)
            .Where(lessonId => lessonId != Guid.Empty)
            .ToHashSet();

        if (manifestModuleIds.Count == 0 || manifestLessonIds.Count == 0)
        {
            return false;
        }

        var persistedModules = record.Modules ?? [];
        var persistedModuleIds = persistedModules
            .Select(module => module.Id)
            .ToHashSet();
        var persistedLessons = persistedModules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToList();
        var persistedLessonIds = persistedLessons
            .Select(lesson => lesson.Id)
            .ToHashSet();

        if (!persistedModuleIds.SetEquals(manifestModuleIds))
        {
            return true;
        }

        if (!persistedLessonIds.SetEquals(manifestLessonIds))
        {
            return true;
        }

        var manifestLessonPaths = manifest.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToDictionary(
                lesson => lesson.LessonId,
                lesson => NormalizePath(ResolveAbsolutePath(lesson, manifest.RootFolderPath)));

        return persistedLessons.Any(lesson =>
            manifestLessonPaths.TryGetValue(lesson.Id, out var manifestPath) &&
            !string.Equals(
                NormalizePath(FirstNonEmpty(lesson.LocalFilePath, lesson.FilePath)),
                manifestPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private static CourseSourceMetadata DeserializeCourseSourceMetadata(persistence.models.CourseRecord record)
    {
        CourseSourceMetadata? metadata = null;

        if (!string.IsNullOrWhiteSpace(record.SourceMetadataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<CourseSourceMetadata>(record.SourceMetadataJson, JsonOptions);
            }
            catch (JsonException)
            {
                metadata = null;
            }
        }

        metadata ??= new CourseSourceMetadata();

        if (record.SourceType == CourseSourceType.LocalFolder &&
            string.IsNullOrWhiteSpace(metadata.RootPath))
        {
            metadata.RootPath = record.FolderPath;
        }

        return metadata;
    }

    private static DetectedCourseStructure? TryDeserializeManifest(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DetectedCourseStructure>(manifestJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DetectedCourseStructure BuildManifestFromCourse(Course course)
    {
        var rootPath = course.FolderPath;
        var rootName = ResolveRootFolderName(course, rootPath);

        var modules = course.Modules
            .OrderBy(module => module.Order)
            .Select(module => new DetectedModuleStructure
            {
                ModuleId = module.Id,
                Order = module.Order,
                RawName = FirstNonEmpty(module.RawTitle, module.Title),
                RelativePath = ".",
                Topics = module.Topics
                    .OrderBy(topic => topic.Order)
                    .Select(topic => new DetectedTopicStructure
                    {
                        TopicId = topic.Id,
                        Order = topic.Order,
                        RawName = FirstNonEmpty(topic.RawTitle, topic.Title),
                        RelativePath = ".",
                        Lessons = topic.Lessons
                            .OrderBy(lesson => lesson.Order)
                            .Select(lesson =>
                            {
                                var absolutePath = FirstNonEmpty(lesson.LocalFilePath, lesson.FilePath);
                                var fileName = Path.GetFileName(absolutePath);
                                var relativePath = ResolveRelativePath(rootPath, absolutePath, fileName);
                                return new DetectedLessonFile
                                {
                                    LessonId = lesson.Id,
                                    Order = lesson.Order,
                                    RawName = FirstNonEmpty(lesson.RawTitle, lesson.Title),
                                    FileName = fileName,
                                    RelativePath = relativePath,
                                    AbsolutePath = absolutePath,
                                    Extension = Path.GetExtension(absolutePath),
                                    FileSizeBytes = 0,
                                    Duration = lesson.Duration
                                };
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        return new DetectedCourseStructure
        {
            CourseId = course.Id,
            RootFolderName = rootName,
            RootFolderPath = rootPath,
            PresentationRootRelativePath = ".",
            ScannedAt = course.SourceMetadata.ImportedAt ?? DateTime.UtcNow,
            RootNode = new DetectedFolderNode
            {
                Name = rootName,
                RelativePath = "."
            },
            Modules = modules
        };
    }

    private static bool HasAnyLessons(persistence.models.CourseRecord record)
    {
        return record.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Any();
    }

    private static bool HasAnyLessons(Course course)
    {
        return course.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Any();
    }

    private static async Task UpsertLocalManifestAsync(StudyHubDbContext context, DetectedCourseStructure manifest)
    {
        var snapshot = await context.CourseImportSnapshots
            .FirstOrDefaultAsync(item => item.CourseId == manifest.CourseId);

        if (snapshot == null)
        {
            snapshot = new persistence.models.CourseImportSnapshotRecord
            {
                CourseId = manifest.CourseId
            };

            await context.CourseImportSnapshots.AddAsync(snapshot);
        }

        snapshot.SourceKind = "local-folder";
        snapshot.RootFolderPath = manifest.RootFolderPath ?? string.Empty;
        snapshot.StructureJson = JsonSerializer.Serialize(manifest, JsonOptions);
        snapshot.ImportedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }

    private static string ResolveRootFolderName(Course course, string rootPath)
    {
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            var normalizedPath = rootPath
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(normalizedPath);
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                return folderName;
            }
        }

        return FirstNonEmpty(course.RawTitle, course.Title, "Curso Local");
    }

    private static string ResolveRelativePath(string rootPath, string absolutePath, string fallbackFileName)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(absolutePath))
        {
            return FirstNonEmpty(fallbackFileName, ".");
        }

        try
        {
            var relative = Path.GetRelativePath(rootPath, absolutePath);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
            {
                return NormalizePath(relative);
            }
        }
        catch (ArgumentException)
        {
        }

        return FirstNonEmpty(fallbackFileName, ".");
    }

    private static string ResolveAbsolutePath(DetectedLessonFile lesson, string rootFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(lesson.AbsolutePath))
        {
            return lesson.AbsolutePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(rootFolderPath) && !string.IsNullOrWhiteSpace(lesson.RelativePath))
        {
            try
            {
                var relativePath = lesson.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(rootFolderPath, relativePath));
            }
            catch (Exception)
            {
            }
        }

        return lesson.FileName.Trim();
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim();
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

    private sealed class PreservedLessonState
    {
        public studyhub.shared.Enums.LessonStatus Status { get; set; }
        public double WatchedPercentage { get; set; }
        public int LastPlaybackPositionSeconds { get; set; }
        public int DurationMinutes { get; set; }
    }
}
