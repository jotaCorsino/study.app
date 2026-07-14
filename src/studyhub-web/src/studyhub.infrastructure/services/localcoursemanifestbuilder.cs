using studyhub.application.Contracts.LocalImport;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

internal static class LocalCourseManifestBuilder
{
    public static DetectedCourseStructure Build(
        CourseRecord course,
        string normalizedRootPath,
        DateTime scannedAtUtc)
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

        var rootName = ResolveRootFolderName(course, rootPath);
        var modules = course.Modules
            .Select(module => BuildModule(module, rootPath))
            .OrderBy(module => module.Order)
            .ThenBy(module => module.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(module => module.ModuleId)
            .ToList();

        var manifest = new DetectedCourseStructure
        {
            CourseId = course.Id,
            RootFolderName = rootName,
            RootFolderPath = rootPath,
            PresentationRootRelativePath = ".",
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

    private static DetectedModuleStructure BuildModule(ModuleRecord module, string rootPath)
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
                .Select(topic => BuildTopic(topic, moduleRelativePath, rootPath))
                .OrderBy(topic => topic.Order)
                .ThenBy(topic => topic.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(topic => topic.TopicId)
                .ToList()
        };
    }

    private static DetectedTopicStructure BuildTopic(
        TopicRecord topic,
        string moduleRelativePath,
        string rootPath)
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
                .Select(lesson => BuildLesson(lesson, rootPath))
                .OrderBy(lesson => lesson.Order)
                .ThenBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(lesson => lesson.LessonId)
                .ToList()
        };
    }

    private static DetectedLessonFile BuildLesson(LessonRecord lesson, string rootPath)
    {
        if (!TryResolveLessonPath(lesson, rootPath, out var relativePath, out var absolutePath))
        {
            throw new InvalidOperationException(
                $"Lesson '{lesson.Id}' does not have a safe path below the local course root.");
        }

        var fileName = relativePath.Split('/').Last();

        return new DetectedLessonFile
        {
            LessonId = lesson.Id,
            Order = lesson.Order,
            RawName = FirstNonEmpty(lesson.RawTitle, lesson.Title, fileName),
            FileName = fileName,
            RelativePath = relativePath,
            AbsolutePath = absolutePath,
            Extension = Path.GetExtension(fileName),
            FileSizeBytes = 0,
            Duration = TimeSpan.FromMinutes(Math.Max(0, lesson.DurationMinutes))
        };
    }

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
