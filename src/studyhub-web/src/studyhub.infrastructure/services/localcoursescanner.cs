using System.Security.Cryptography;
using System.Text;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;

namespace studyhub.infrastructure.services;

internal sealed class LocalCourseScanner(IVideoMetadataReader videoMetadataReader)
{
    private static readonly string[] VideoExtensions =
    [
        ".mp4",
        ".m4v",
        ".mov",
        ".mkv",
        ".avi",
        ".wmv",
        ".webm"
    ];

    private readonly IVideoMetadataReader _videoMetadataReader = videoMetadataReader;

    public async Task<DetectedCourseStructure> ScanAsync(string rootFolderPath, CancellationToken cancellationToken = default)
    {
        var normalizedRootPath = Path.GetFullPath(rootFolderPath);
        var rootDirectory = new DirectoryInfo(normalizedRootPath);

        if (!rootDirectory.Exists)
        {
            throw new DirectoryNotFoundException($"A pasta \"{normalizedRootPath}\" não foi encontrada.");
        }

        var rootToken = NormalizeToken(normalizedRootPath);
        var rootNode = await BuildFolderNodeAsync(rootDirectory, rootDirectory.FullName, rootToken, cancellationToken);
        var presentationRoot = ResolvePresentationRoot(rootNode);
        var modules = BuildModules(presentationRoot, rootDirectory.Name, rootToken);

        return new DetectedCourseStructure
        {
            CourseId = CreateDeterministicGuid($"course|{rootToken}"),
            RootFolderName = rootDirectory.Name,
            RootFolderPath = normalizedRootPath,
            PresentationRootRelativePath = presentationRoot.RelativePath,
            ScannedAt = DateTime.UtcNow,
            RootNode = rootNode,
            Modules = modules
        };
    }

    private async Task<DetectedFolderNode> BuildFolderNodeAsync(
        DirectoryInfo directory,
        string rootDirectoryPath,
        string rootToken,
        CancellationToken cancellationToken)
    {
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootDirectoryPath, directory.FullName));
        var node = new DetectedFolderNode
        {
            Name = directory.Name,
            RelativePath = relativePath
        };

        var directVideoFiles = directory.GetFiles()
            .Where(IsVideoFile)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        node.DirectLessons = await BuildLessonsAsync(directVideoFiles, rootDirectoryPath, rootToken, cancellationToken);

        var childDirectories = directory.GetDirectories()
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var childDirectory in childDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Children.Add(await BuildFolderNodeAsync(childDirectory, rootDirectoryPath, rootToken, cancellationToken));
        }

        return node;
    }

    private static DetectedFolderNode ResolvePresentationRoot(DetectedFolderNode rootNode)
    {
        var current = rootNode;

        while (current.DirectLessons.Count == 0 && current.Children.Count == 1)
        {
            current = current.Children[0];
        }

        return current;
    }

    private static List<DetectedModuleStructure> BuildModules(
        DetectedFolderNode presentationRoot,
        string courseFolderName,
        string rootToken)
    {
        var modules = new List<DetectedModuleStructure>();
        var moduleOrder = 1;

        if (presentationRoot.DirectLessons.Count > 0)
        {
            modules.Add(BuildModuleFromLessons(
                presentationRoot.RelativePath,
                courseFolderName,
                courseFolderName,
                presentationRoot.DirectLessons,
                moduleOrder++,
                rootToken));
        }

        foreach (var child in presentationRoot.Children.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var module = BuildModule(child, courseFolderName, moduleOrder, rootToken);
            if (module.Topics.Count == 0)
            {
                continue;
            }

            modules.Add(module);
            moduleOrder++;
        }

        if (modules.Count == 0)
        {
            modules.Add(BuildModuleFromLessons(
                presentationRoot.RelativePath,
                courseFolderName,
                courseFolderName,
                EnumerateLessons(presentationRoot),
                moduleOrder,
                rootToken));
        }

        return modules;
    }

    private static DetectedModuleStructure BuildModuleFromLessons(
        string moduleRelativePath,
        string moduleRawName,
        string courseFolderName,
        IReadOnlyCollection<DetectedLessonFile> lessons,
        int moduleOrder,
        string rootToken)
    {
        return new DetectedModuleStructure
        {
            ModuleId = CreateDeterministicGuid($"module|{rootToken}|{NormalizeToken(moduleRelativePath)}"),
            Order = moduleOrder,
            RawName = moduleRawName,
            RelativePath = moduleRelativePath,
            Topics =
            [
                new DetectedTopicStructure
                {
                    TopicId = CreateDeterministicGuid($"topic|{rootToken}|{NormalizeToken(moduleRelativePath)}|."),
                    Order = 1,
                    RawName = courseFolderName,
                    RelativePath = ".",
                    Lessons = lessons
                        .OrderBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .Select((lesson, index) =>
                        {
                            lesson.Order = index + 1;
                            return lesson;
                        })
                        .ToList()
                }
            ]
        };
    }

    private static DetectedModuleStructure BuildModule(
        DetectedFolderNode moduleRoot,
        string courseFolderName,
        int moduleOrder,
        string rootToken)
    {
        var lessonGroups = EnumerateLessons(moduleRoot)
            .GroupBy(lesson =>
            {
                var moduleRelativePath = NormalizeRelativePath(Path.GetDirectoryName(lesson.RelativePath) ?? ".");
                var relativeInsideModule = moduleRelativePath.StartsWith(moduleRoot.RelativePath, StringComparison.OrdinalIgnoreCase)
                    ? NormalizeRelativePath(moduleRelativePath[moduleRoot.RelativePath.Length..].TrimStart('/', '\\'))
                    : ".";
                return string.IsNullOrWhiteSpace(relativeInsideModule) ? "." : relativeInsideModule;
            })
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topics = new List<DetectedTopicStructure>();
        var topicOrder = 1;

        foreach (var lessonGroup in lessonGroups)
        {
            var topicRelativePath = NormalizeRelativePath(lessonGroup.Key);
            var rawTopicName = topicRelativePath == "." ? moduleRoot.Name : topicRelativePath;

            topics.Add(new DetectedTopicStructure
            {
                TopicId = CreateDeterministicGuid($"topic|{rootToken}|{NormalizeToken(moduleRoot.RelativePath)}|{NormalizeToken(topicRelativePath)}"),
                Order = topicOrder++,
                RawName = rawTopicName,
                RelativePath = topicRelativePath,
                Lessons = lessonGroup
                    .OrderBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select((lesson, index) =>
                    {
                        lesson.Order = index + 1;
                        return lesson;
                    })
                    .ToList()
            });
        }

        return new DetectedModuleStructure
        {
            ModuleId = CreateDeterministicGuid($"module|{rootToken}|{NormalizeToken(moduleRoot.RelativePath)}"),
            Order = moduleOrder,
            RawName = moduleRoot.Name,
            RelativePath = moduleRoot.RelativePath,
            Topics = topics
        };
    }

    private static List<DetectedLessonFile> EnumerateLessons(DetectedFolderNode node)
    {
        var lessons = new List<DetectedLessonFile>();
        AppendLessons(node, lessons);

        return lessons
            .OrderBy(lesson => lesson.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendLessons(DetectedFolderNode node, List<DetectedLessonFile> lessons)
    {
        lessons.AddRange(node.DirectLessons);

        foreach (var child in node.Children.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            AppendLessons(child, lessons);
        }
    }

    private async Task<List<DetectedLessonFile>> BuildLessonsAsync(
        IEnumerable<FileInfo> files,
        string rootDirectoryPath,
        string rootToken,
        CancellationToken cancellationToken)
    {
        var lessons = new List<DetectedLessonFile>();
        var lessonOrder = 1;

        foreach (var file in files.OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var duration = await _videoMetadataReader.TryReadDurationAsync(file.FullName, cancellationToken) ?? TimeSpan.Zero;
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootDirectoryPath, file.FullName));

            lessons.Add(new DetectedLessonFile
            {
                LessonId = CreateDeterministicGuid($"lesson|{rootToken}|{NormalizeToken(relativePath)}"),
                Order = lessonOrder++,
                RawName = Path.GetFileNameWithoutExtension(file.Name),
                FileName = file.Name,
                RelativePath = relativePath,
                AbsolutePath = file.FullName,
                Extension = file.Extension,
                FileSizeBytes = file.Length,
                Duration = duration
            });
        }

        return lessons;
    }

    private static bool IsVideoFile(FileInfo file)
        => IsVideoPath(file.FullName);

    private static bool IsVideoPath(string path)
        => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static string NormalizeDisplayName(string rawName, bool stripExtension = false)
    {
        var source = stripExtension ? Path.GetFileNameWithoutExtension(rawName) : rawName;
        var segments = source
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(segment =>
            {
                var cleaned = segment.Replace('_', ' ').Replace('.', ' ').Trim();
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^\d+\s*[-_.)]*\s*", string.Empty);
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
                return string.IsNullOrWhiteSpace(cleaned) ? segment : cleaned;
            })
            .ToList();

        return segments.Count == 0 ? source : string.Join(" / ", segments);
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    private static string NormalizeToken(string value)
        => value.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Trim()
            .ToLowerInvariant();

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ".")
        {
            return ".";
        }

        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Trim('/');
    }
}
