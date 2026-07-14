using studyhub.application.Contracts.LocalImport;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

internal static class LocalCourseManifestBuilder
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static DetectedCourseStructure Build(
        CourseRecord course,
        string normalizedRootPath,
        DateTime scannedAtUtc)
        => Build(
            course,
            normalizedRootPath,
            scannedAtUtc,
            detectedStructure: null,
            previousManifest: null);

    public static DetectedCourseStructure Build(
        CourseRecord course,
        string normalizedRootPath,
        DateTime scannedAtUtc,
        DetectedCourseStructure? detectedStructure,
        DetectedCourseStructure? previousManifest = null)
    {
        ArgumentNullException.ThrowIfNull(course);

        if (course.Id == Guid.Empty)
        {
            throw new ArgumentException("The persisted course must have an identity.", nameof(course));
        }

        if (!LocalCourseSourceRootResolver.TryNormalize(normalizedRootPath, out var rootPath))
        {
            throw new ArgumentException(
                "The local course root must be a fully qualified path.",
                nameof(normalizedRootPath));
        }

        if (scannedAtUtc == default)
        {
            throw new ArgumentException("The manifest scan time is required.", nameof(scannedAtUtc));
        }

        var currentDetectedStructure = HasMatchingRoot(detectedStructure, rootPath)
            ? detectedStructure
            : null;
        var previousCourseManifest = previousManifest?.CourseId == course.Id
            ? previousManifest
            : null;
        var detectedLessonsByPath = CreateLessonMetadataIndex(currentDetectedStructure);
        var previousLessonsByPath = CreateLessonMetadataIndex(previousCourseManifest);
        var rootName = ResolveRootFolderName(course, rootPath);
        var modules = course.Modules
            .Select(module => BuildModule(
                module,
                rootPath,
                detectedLessonsByPath,
                previousLessonsByPath))
            .OrderBy(module => module.Order)
            .ThenBy(module => module.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(module => module.ModuleId)
            .ToList();

        var manifest = new DetectedCourseStructure
        {
            CourseId = course.Id,
            RootFolderName = rootName,
            RootFolderPath = rootPath,
            PresentationRootRelativePath = ResolvePresentationRootRelativePath(
                currentDetectedStructure,
                previousCourseManifest),
            ScannedAt = NormalizeUtc(scannedAtUtc),
            RootNode = new DetectedFolderNode
            {
                Name = rootName,
                RelativePath = "."
            },
            Modules = modules
        };

        PopulateRootNode(manifest.RootNode, modules);
        return manifest;
    }

    private static DetectedModuleStructure BuildModule(
        ModuleRecord module,
        string rootPath,
        IReadOnlyDictionary<string, DetectedLessonFile> detectedLessonsByPath,
        IReadOnlyDictionary<string, DetectedLessonFile> previousLessonsByPath)
    {
        var moduleRelativePath = LocalCourseStructurePathHelper.TryNormalize(
            module.SourceRelativePath,
            out var normalizedModulePath)
            ? normalizedModulePath
            : ".";

        return new DetectedModuleStructure
        {
            ModuleId = module.Id,
            Order = module.Order,
            RawName = FirstNonEmpty(module.RawTitle, module.Title),
            RelativePath = moduleRelativePath,
            Topics = module.Topics
                .Select(topic => BuildTopic(
                    topic,
                    moduleRelativePath,
                    rootPath,
                    detectedLessonsByPath,
                    previousLessonsByPath))
                .OrderBy(topic => topic.Order)
                .ThenBy(topic => topic.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(topic => topic.TopicId)
                .ToList()
        };
    }

    private static DetectedTopicStructure BuildTopic(
        TopicRecord topic,
        string moduleRelativePath,
        string rootPath,
        IReadOnlyDictionary<string, DetectedLessonFile> detectedLessonsByPath,
        IReadOnlyDictionary<string, DetectedLessonFile> previousLessonsByPath)
    {
        var topicRelativePath = LocalCourseStructurePathHelper.TryMakeRelativeToParent(
            moduleRelativePath,
            topic.SourceRelativePath,
            out var normalizedTopicPath)
            ? normalizedTopicPath
            : ".";

        return new DetectedTopicStructure
        {
            TopicId = topic.Id,
            Order = topic.Order,
            RawName = FirstNonEmpty(topic.RawTitle, topic.Title),
            RelativePath = topicRelativePath,
            Lessons = topic.Lessons
                .Select(lesson => BuildLesson(
                    lesson,
                    rootPath,
                    detectedLessonsByPath,
                    previousLessonsByPath))
                .OrderBy(lesson => lesson.Order)
                .ThenBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(lesson => lesson.LessonId)
                .ToList()
        };
    }

    private static DetectedLessonFile BuildLesson(
        LessonRecord lesson,
        string rootPath,
        IReadOnlyDictionary<string, DetectedLessonFile> detectedLessonsByPath,
        IReadOnlyDictionary<string, DetectedLessonFile> previousLessonsByPath)
    {
        if (!TryResolveLessonPath(lesson, rootPath, out var relativePath, out var absolutePath))
        {
            throw new InvalidOperationException(
                $"Lesson '{lesson.Id}' does not have a safe path below the local course root.");
        }

        var fileName = relativePath.Split('/').Last();
        detectedLessonsByPath.TryGetValue(relativePath, out var detectedLesson);
        previousLessonsByPath.TryGetValue(relativePath, out var previousLesson);

        return new DetectedLessonFile
        {
            LessonId = lesson.Id,
            Order = lesson.Order,
            RawName = FirstNonEmpty(lesson.RawTitle, lesson.Title, fileName),
            FileName = fileName,
            RelativePath = relativePath,
            AbsolutePath = absolutePath,
            Extension = Path.GetExtension(fileName),
            FileSizeBytes = ResolveFileSizeBytes(detectedLesson, previousLesson),
            Duration = ResolveDuration(detectedLesson, previousLesson, lesson.DurationMinutes)
        };
    }

    private static IReadOnlyDictionary<string, DetectedLessonFile> CreateLessonMetadataIndex(
        DetectedCourseStructure? structure)
    {
        var index = new Dictionary<string, DetectedLessonFile>(PathComparer);
        if (structure?.Modules is null)
        {
            return index;
        }

        var ambiguousPaths = new HashSet<string>(PathComparer);
        foreach (var lesson in structure.Modules
                     .Where(module => module?.Topics is not null)
                     .SelectMany(module => module.Topics)
                     .Where(topic => topic?.Lessons is not null)
                     .SelectMany(topic => topic.Lessons))
        {
            if (lesson is null ||
                !LocalLessonPathHelper.TryNormalizePortableRelativePath(
                    lesson.RelativePath,
                    out var relativePath) ||
                ambiguousPaths.Contains(relativePath))
            {
                continue;
            }

            if (!index.TryAdd(relativePath, lesson))
            {
                index.Remove(relativePath);
                ambiguousPaths.Add(relativePath);
            }
        }

        return index;
    }

    private static long ResolveFileSizeBytes(
        DetectedLessonFile? detectedLesson,
        DetectedLessonFile? previousLesson)
    {
        if (detectedLesson?.FileSizeBytes >= 0)
        {
            return detectedLesson.FileSizeBytes;
        }

        return previousLesson?.FileSizeBytes >= 0
            ? previousLesson.FileSizeBytes
            : 0;
    }

    private static TimeSpan ResolveDuration(
        DetectedLessonFile? detectedLesson,
        DetectedLessonFile? previousLesson,
        int persistedDurationMinutes)
    {
        if (detectedLesson?.Duration > TimeSpan.Zero)
        {
            return detectedLesson.Duration;
        }

        if (previousLesson?.Duration > TimeSpan.Zero)
        {
            return previousLesson.Duration;
        }

        return TimeSpan.FromMinutes(Math.Max(0, persistedDurationMinutes));
    }

    private static string ResolvePresentationRootRelativePath(
        DetectedCourseStructure? detectedStructure,
        DetectedCourseStructure? previousManifest)
    {
        if (LocalCourseStructurePathHelper.TryNormalize(
                detectedStructure?.PresentationRootRelativePath,
                out var detectedPresentationRoot))
        {
            return detectedPresentationRoot;
        }

        return LocalCourseStructurePathHelper.TryNormalize(
            previousManifest?.PresentationRootRelativePath,
            out var previousPresentationRoot)
            ? previousPresentationRoot
            : ".";
    }

    private static bool HasMatchingRoot(
        DetectedCourseStructure? detectedStructure,
        string rootPath)
        => detectedStructure is not null &&
           LocalCourseSourceRootResolver.TryNormalize(
               detectedStructure.RootFolderPath,
               out var detectedRootPath) &&
           PathComparer.Equals(detectedRootPath, rootPath);

    private static bool TryResolveLessonPath(
        LessonRecord lesson,
        string rootPath,
        out string relativePath,
        out string absolutePath)
    {
        relativePath = string.Empty;
        absolutePath = string.Empty;

        if (LocalLessonPathHelper.TryNormalizePortableRelativePath(
                lesson.RelativeFilePath,
                out relativePath) &&
            LocalLessonPathHelper.TryResolvePhysicalPath(
                rootPath,
                relativePath,
                out absolutePath))
        {
            return true;
        }

        foreach (var storedAbsolutePath in new[] { lesson.LocalFilePath, lesson.FilePath })
        {
            if (LocalLessonPathHelper.TryCalculatePortableRelativePath(
                    rootPath,
                    storedAbsolutePath,
                    out relativePath) &&
                LocalLessonPathHelper.TryResolvePhysicalPath(
                    rootPath,
                    relativePath,
                    out absolutePath))
            {
                return true;
            }
        }

        relativePath = string.Empty;
        absolutePath = string.Empty;
        return false;
    }

    private static void PopulateRootNode(
        DetectedFolderNode rootNode,
        IEnumerable<DetectedModuleStructure> modules)
    {
        foreach (var lesson in modules
                     .SelectMany(module => module.Topics)
                     .SelectMany(topic => topic.Lessons)
                     .OrderBy(lesson => lesson.Order)
                     .ThenBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(lesson => lesson.LessonId))
        {
            var segments = lesson.RelativePath.Split('/');
            var currentNode = rootNode;
            var currentRelativePath = string.Empty;

            foreach (var directoryName in segments.Take(segments.Length - 1))
            {
                currentRelativePath = string.IsNullOrEmpty(currentRelativePath)
                    ? directoryName
                    : $"{currentRelativePath}/{directoryName}";

                var childNode = currentNode.Children.FirstOrDefault(child =>
                    string.Equals(
                        child.RelativePath,
                        currentRelativePath,
                        StringComparison.OrdinalIgnoreCase));

                if (childNode is null)
                {
                    childNode = new DetectedFolderNode
                    {
                        Name = directoryName,
                        RelativePath = currentRelativePath
                    };
                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }

            currentNode.DirectLessons.Add(CloneLesson(lesson));
        }

        SortRootNode(rootNode);
    }

    private static DetectedLessonFile CloneLesson(DetectedLessonFile lesson)
        => new()
        {
            LessonId = lesson.LessonId,
            Order = lesson.Order,
            RawName = lesson.RawName,
            FileName = lesson.FileName,
            RelativePath = lesson.RelativePath,
            AbsolutePath = lesson.AbsolutePath,
            Extension = lesson.Extension,
            FileSizeBytes = lesson.FileSizeBytes,
            Duration = lesson.Duration
        };

    private static void SortRootNode(DetectedFolderNode node)
    {
        node.Children = node.Children
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        node.DirectLessons = node.DirectLessons
            .OrderBy(lesson => lesson.Order)
            .ThenBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(lesson => lesson.LessonId)
            .ToList();

        foreach (var child in node.Children)
        {
            SortRootNode(child);
        }
    }

    private static string ResolveRootFolderName(CourseRecord course, string rootPath)
    {
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));
        return FirstNonEmpty(rootName, course.RawTitle, course.Title, "Curso Local");
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

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
