using studyhub.application.Contracts.CourseContentSync;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;
using studyhub.shared.Enums;

namespace studyhub.infrastructure.services;

internal sealed class CourseContentSyncApplier
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly TopicKeyComparer TopicKeys = new();

    public CourseContentSyncApplySummary Apply(
        CourseRecord course,
        CourseContentSyncPreviewResult plan,
        string normalizedRootPath)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.Success)
        {
            throw new InvalidOperationException("Only a successful current sync plan can be applied.");
        }

        var summary = new CourseContentSyncApplySummary();
        var modulesById = course.Modules.ToDictionary(module => module.Id);
        var topicsById = course.Modules
            .SelectMany(module => module.Topics)
            .ToDictionary(topic => topic.Id);
        var lessonsById = course.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToDictionary(lesson => lesson.Id);

        ValidateExistingReferences(plan, modulesById, topicsById, lessonsById);

        var modulesByPath = new Dictionary<string, ModuleRecord>(PathComparer);
        foreach (var item in plan.Modules)
        {
            ModuleRecord module;
            if (item.ChangeKind == CourseContentSyncChangeKind.New)
            {
                module = CreateModule(course.Id, item);
                course.Modules.Add(module);
                summary.CreatedModules.Add(module);
                summary.CreatedModuleCount++;
                summary.ContentChanged = true;
            }
            else
            {
                module = modulesById[item.ExistingModuleId!.Value];
                ApplyExistingModule(module, item, summary);
            }

            if (!modulesByPath.TryAdd(item.SourceRelativePath, module))
            {
                throw new InvalidOperationException("The sync module identity map contains a duplicate path.");
            }
        }

        var topicsByPath = new Dictionary<TopicKey, TopicRecord>(TopicKeys);
        foreach (var item in plan.Topics)
        {
            TopicRecord topic;
            if (item.ChangeKind == CourseContentSyncChangeKind.New)
            {
                if (!modulesByPath.TryGetValue(item.ModuleSourceRelativePath, out var parentModule))
                {
                    throw new InvalidOperationException("The parent module for a new sync topic was not found.");
                }

                topic = CreateTopic(parentModule, item);
                parentModule.Topics.Add(topic);
                summary.CreatedTopics.Add(topic);
                summary.CreatedTopicCount++;
                summary.ContentChanged = true;
            }
            else
            {
                topic = topicsById[item.ExistingTopicId!.Value];
                ApplyExistingTopic(topic, item, summary);
            }

            var key = new TopicKey(item.ModuleSourceRelativePath, item.SourceRelativePath);
            if (!topicsByPath.TryAdd(key, topic))
            {
                throw new InvalidOperationException("The sync topic identity map contains a duplicate path.");
            }
        }

        foreach (var item in plan.Lessons)
        {
            if (item.ChangeKind == CourseContentSyncChangeKind.New)
            {
                var topicKey = new TopicKey(
                    item.ModuleSourceRelativePath,
                    item.TopicSourceRelativePath);
                if (!topicsByPath.TryGetValue(topicKey, out var parentTopic))
                {
                    throw new InvalidOperationException("The parent topic for a new sync lesson was not found.");
                }

                var newLesson = CreateLesson(
                    parentTopic.Id,
                    item,
                    normalizedRootPath);
                parentTopic.Lessons.Add(newLesson);
                summary.CreatedLessons.Add(newLesson);
                summary.CreatedLessonCount++;
                summary.ContentChanged = true;
                continue;
            }

            var lesson = lessonsById[item.ExistingLessonId!.Value];
            ApplyExistingLesson(lesson, item, normalizedRootPath, summary);
        }

        return summary;
    }

    private static void ValidateExistingReferences(
        CourseContentSyncPreviewResult plan,
        IReadOnlyDictionary<Guid, ModuleRecord> modules,
        IReadOnlyDictionary<Guid, TopicRecord> topics,
        IReadOnlyDictionary<Guid, LessonRecord> lessons)
    {
        if (plan.Modules.Any(item =>
                item.ChangeKind != CourseContentSyncChangeKind.New &&
                (!item.ExistingModuleId.HasValue || !modules.ContainsKey(item.ExistingModuleId.Value))) ||
            plan.Topics.Any(item =>
                item.ChangeKind != CourseContentSyncChangeKind.New &&
                (!item.ExistingTopicId.HasValue || !topics.ContainsKey(item.ExistingTopicId.Value))) ||
            plan.Lessons.Any(item =>
                item.ChangeKind != CourseContentSyncChangeKind.New &&
                (!item.ExistingLessonId.HasValue || !lessons.ContainsKey(item.ExistingLessonId.Value))))
        {
            throw new InvalidOperationException("The current sync plan does not match the tracked course tree.");
        }
    }

    private static void ApplyExistingModule(
        ModuleRecord module,
        CourseContentSyncModulePlanItem item,
        CourseContentSyncApplySummary summary)
    {
        if (item.ChangeKind == CourseContentSyncChangeKind.Missing)
        {
            summary.ContentChanged |= SetIfDifferent(
                module.SourceRelativePath,
                item.SourceRelativePath,
                value => module.SourceRelativePath = value,
                StringComparison.Ordinal);
            if (module.IsAvailable)
            {
                module.IsAvailable = false;
                summary.MarkedMissingModuleCount++;
                summary.ContentChanged = true;
            }

            return;
        }

        RestoreAvailability(module.IsAvailable, value => module.IsAvailable = value, summary);
        summary.ContentChanged |= SetIfDifferent(
            module.SourceRelativePath,
            item.SourceRelativePath,
            value => module.SourceRelativePath = value,
            StringComparison.Ordinal);
        if (item.DetectedOrder is > 0 and var detectedOrder && module.Order != detectedOrder)
        {
            module.Order = detectedOrder;
            summary.ContentChanged = true;
        }
    }

    private static void ApplyExistingTopic(
        TopicRecord topic,
        CourseContentSyncTopicPlanItem item,
        CourseContentSyncApplySummary summary)
    {
        if (item.ChangeKind == CourseContentSyncChangeKind.Missing)
        {
            summary.ContentChanged |= SetIfDifferent(
                topic.SourceRelativePath,
                item.SourceRelativePath,
                value => topic.SourceRelativePath = value,
                StringComparison.Ordinal);
            if (topic.IsAvailable)
            {
                topic.IsAvailable = false;
                summary.MarkedMissingTopicCount++;
                summary.ContentChanged = true;
            }

            return;
        }

        RestoreAvailability(topic.IsAvailable, value => topic.IsAvailable = value, summary);
        summary.ContentChanged |= SetIfDifferent(
            topic.SourceRelativePath,
            item.SourceRelativePath,
            value => topic.SourceRelativePath = value,
            StringComparison.Ordinal);
        if (item.DetectedOrder is > 0 and var detectedOrder && topic.Order != detectedOrder)
        {
            topic.Order = detectedOrder;
            summary.ContentChanged = true;
        }
    }

    private static void ApplyExistingLesson(
        LessonRecord lesson,
        CourseContentSyncLessonPlanItem item,
        string normalizedRootPath,
        CourseContentSyncApplySummary summary)
    {
        if (item.ChangeKind == CourseContentSyncChangeKind.Missing)
        {
            summary.ContentChanged |= SetIfDifferent(
                lesson.RelativeFilePath,
                item.RelativeFilePath,
                value => lesson.RelativeFilePath = value,
                StringComparison.Ordinal);
            if (lesson.IsAvailable)
            {
                lesson.IsAvailable = false;
                summary.MarkedMissingLessonCount++;
                summary.ContentChanged = true;
            }

            return;
        }

        RestoreAvailability(lesson.IsAvailable, value => lesson.IsAvailable = value, summary);
        summary.ContentChanged |= SetIfDifferent(
            lesson.RelativeFilePath,
            item.RelativeFilePath,
            value => lesson.RelativeFilePath = value,
            StringComparison.Ordinal);
        if (item.DetectedOrder is > 0 and var detectedOrder && lesson.Order != detectedOrder)
        {
            lesson.Order = detectedOrder;
            summary.ContentChanged = true;
        }

        var absolutePath = ResolveAbsolutePath(normalizedRootPath, item.RelativeFilePath);
        summary.ContentChanged |= SetIfDifferent(
            lesson.LocalFilePath,
            absolutePath,
            value => lesson.LocalFilePath = value,
            PathComparison);
        summary.ContentChanged |= SetIfDifferent(
            lesson.FilePath,
            absolutePath,
            value => lesson.FilePath = value,
            PathComparison);

        var detectedDurationMinutes = ConvertDetectedDuration(item.DetectedDuration);
        if (detectedDurationMinutes > 0 && lesson.DurationMinutes != detectedDurationMinutes)
        {
            lesson.DurationMinutes = detectedDurationMinutes;
            summary.ContentChanged = true;
        }
    }

    private static ModuleRecord CreateModule(
        Guid courseId,
        CourseContentSyncModulePlanItem item)
    {
        var rawTitle = ResolveRawTitle(item.DetectedName, item.SourceRelativePath, "Novo módulo");
        return new ModuleRecord
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Order = ResolveDetectedOrder(item.DetectedOrder),
            RawTitle = rawTitle,
            RawDescription = string.Empty,
            Title = LocalCourseScanner.NormalizeDisplayName(rawTitle),
            Description = string.Empty,
            SourceRelativePath = item.SourceRelativePath,
            IsAvailable = true
        };
    }

    private static TopicRecord CreateTopic(
        ModuleRecord parentModule,
        CourseContentSyncTopicPlanItem item)
    {
        var isDirectModuleTopic = PathComparer.Equals(
            item.ModuleSourceRelativePath,
            item.SourceRelativePath);
        var rawTitle = isDirectModuleTopic
            ? ResolveRawTitle(parentModule.RawTitle, parentModule.SourceRelativePath, "Novo tópico")
            : ResolveRawTitle(item.DetectedName, item.SourceRelativePath, "Novo tópico");
        var title = isDirectModuleTopic && !string.IsNullOrWhiteSpace(parentModule.Title)
            ? parentModule.Title
            : LocalCourseScanner.NormalizeDisplayName(rawTitle);
        return new TopicRecord
        {
            Id = Guid.NewGuid(),
            ModuleId = parentModule.Id,
            Order = ResolveDetectedOrder(item.DetectedOrder),
            RawTitle = rawTitle,
            RawDescription = string.Empty,
            Title = title,
            Description = string.Empty,
            SourceRelativePath = item.SourceRelativePath,
            CompletedAtUtc = null,
            IsAvailable = true
        };
    }

    private static LessonRecord CreateLesson(
        Guid topicId,
        CourseContentSyncLessonPlanItem item,
        string normalizedRootPath)
    {
        var rawTitle = ResolveRawTitle(item.DetectedName, item.RelativeFilePath, "Nova aula");
        var absolutePath = ResolveAbsolutePath(normalizedRootPath, item.RelativeFilePath);
        return new LessonRecord
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            Order = ResolveDetectedOrder(item.DetectedOrder),
            RawTitle = rawTitle,
            RawDescription = string.Empty,
            Title = LocalCourseScanner.NormalizeDisplayName(rawTitle, stripExtension: true),
            Description = string.Empty,
            FilePath = absolutePath,
            SourceType = LessonSourceType.LocalFile,
            LocalFilePath = absolutePath,
            RelativeFilePath = item.RelativeFilePath,
            Provider = "LocalFileSystem",
            DurationMinutes = ConvertDetectedDuration(item.DetectedDuration),
            Status = LessonStatus.NotStarted,
            WatchedPercentage = 0,
            LastPlaybackPositionSeconds = 0,
            IsAvailable = true
        };
    }

    private static void RestoreAvailability(
        bool isAvailable,
        Action<bool> setAvailability,
        CourseContentSyncApplySummary summary)
    {
        if (isAvailable)
        {
            return;
        }

        setAvailability(true);
        summary.RestoredAvailableItemCount++;
        summary.ContentChanged = true;
    }

    private static bool SetIfDifferent(
        string current,
        string replacement,
        Action<string> setValue,
        StringComparison comparison)
    {
        if (string.Equals(current, replacement, comparison))
        {
            return false;
        }

        setValue(replacement);
        return true;
    }

    private static string ResolveAbsolutePath(string rootPath, string relativeFilePath)
    {
        if (!LocalLessonPathHelper.TryResolvePhysicalPath(
                rootPath,
                relativeFilePath,
                out var absolutePath))
        {
            throw new InvalidOperationException("A sync lesson path could not be resolved inside the course root.");
        }

        return absolutePath;
    }

    private static string ResolveRawTitle(
        string detectedName,
        string relativePath,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(detectedName))
        {
            return detectedName.Trim();
        }

        var pathName = Path.GetFileName(relativePath.TrimEnd('/'));
        return string.IsNullOrWhiteSpace(pathName) || pathName == "."
            ? fallback
            : pathName;
    }

    private static int ResolveDetectedOrder(int? detectedOrder)
        => detectedOrder is > 0 ? detectedOrder.Value : int.MaxValue;

    private static int ConvertDetectedDuration(TimeSpan? duration)
    {
        if (!duration.HasValue || duration.Value <= TimeSpan.Zero)
        {
            return 0;
        }

        var roundedMinutes = Math.Round(duration.Value.TotalMinutes);
        return (int)Math.Clamp(roundedMinutes, 1, int.MaxValue);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly record struct TopicKey(
        string ModuleSourceRelativePath,
        string TopicSourceRelativePath);

    private sealed class TopicKeyComparer : IEqualityComparer<TopicKey>
    {
        public bool Equals(TopicKey x, TopicKey y)
            => PathComparer.Equals(x.ModuleSourceRelativePath, y.ModuleSourceRelativePath) &&
               PathComparer.Equals(x.TopicSourceRelativePath, y.TopicSourceRelativePath);

        public int GetHashCode(TopicKey obj)
            => HashCode.Combine(
                PathComparer.GetHashCode(obj.ModuleSourceRelativePath),
                PathComparer.GetHashCode(obj.TopicSourceRelativePath));
    }
}

internal sealed class CourseContentSyncApplySummary
{
    public List<ModuleRecord> CreatedModules { get; } = [];
    public List<TopicRecord> CreatedTopics { get; } = [];
    public List<LessonRecord> CreatedLessons { get; } = [];
    public int CreatedModuleCount { get; set; }
    public int CreatedTopicCount { get; set; }
    public int CreatedLessonCount { get; set; }
    public int MarkedMissingModuleCount { get; set; }
    public int MarkedMissingTopicCount { get; set; }
    public int MarkedMissingLessonCount { get; set; }
    public int RestoredAvailableItemCount { get; set; }
    public bool ContentChanged { get; set; }
}
