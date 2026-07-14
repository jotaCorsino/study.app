using studyhub.application.Contracts.CourseContentSync;
using studyhub.application.Contracts.LocalImport;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

internal sealed class CourseContentSyncPlanner
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly TopicKeyComparer TopicKeys = new();
    private static readonly LessonKeyComparer LessonKeys = new();

    public CourseContentSyncPreviewResult Plan(
        CourseRecord persistedCourse,
        DetectedCourseStructure detectedStructure,
        string normalizedRootPath)
    {
        ArgumentNullException.ThrowIfNull(persistedCourse);
        ArgumentNullException.ThrowIfNull(detectedStructure);

        if (detectedStructure.LessonCount == 0)
        {
            return CreateResult(
                persistedCourse,
                detectedStructure,
                normalizedRootPath,
                CourseContentSyncPreviewStatus.NoVideosFound,
                "A pasta atual do curso não contém vídeos reconhecidos.");
        }

        var existingPreparation = PrepareExistingStructure(persistedCourse, normalizedRootPath);
        if (existingPreparation.Diagnostics.Count > 0)
        {
            return CreateResult(
                persistedCourse,
                detectedStructure,
                normalizedRootPath,
                CourseContentSyncPreviewStatus.InsufficientReferenceData,
                "A estrutura persistida não possui referências físicas suficientes para uma comparação segura.",
                existingPreparation.Diagnostics);
        }

        var candidatePreparation = PrepareCandidateStructure(detectedStructure);
        if (candidatePreparation.Diagnostics.Count > 0)
        {
            return CreateResult(
                persistedCourse,
                detectedStructure,
                normalizedRootPath,
                CourseContentSyncPreviewStatus.AmbiguousStructure,
                "A estrutura detectada não pôde ser comparada com segurança.",
                candidatePreparation.Diagnostics);
        }

        var duplicateDiagnostics = FindDuplicateKeys(existingPreparation.Structure, candidatePreparation.Structure);
        if (duplicateDiagnostics.Count > 0)
        {
            return CreateResult(
                persistedCourse,
                detectedStructure,
                normalizedRootPath,
                CourseContentSyncPreviewStatus.AmbiguousStructure,
                "Foram encontradas chaves físicas duplicadas na estrutura do curso.",
                duplicateDiagnostics);
        }

        return BuildPlan(
            persistedCourse,
            detectedStructure,
            normalizedRootPath,
            existingPreparation.Structure,
            candidatePreparation.Structure);
    }

    private static ExistingPreparation PrepareExistingStructure(
        CourseRecord course,
        string normalizedRootPath)
    {
        var diagnostics = new List<string>();
        var lessonPaths = new Dictionary<LessonRecord, string>(ReferenceEqualityComparer.Instance);
        var topicPaths = new Dictionary<TopicRecord, string>(ReferenceEqualityComparer.Instance);
        var modulePaths = new Dictionary<ModuleRecord, string>(ReferenceEqualityComparer.Instance);

        foreach (var lesson in course.Modules
                     .SelectMany(module => module.Topics)
                     .SelectMany(topic => topic.Lessons))
        {
            if (TryGetComparableLessonRelativePath(lesson, normalizedRootPath, out var relativeFilePath))
            {
                lessonPaths[lesson] = relativeFilePath;
            }
            else
            {
                diagnostics.Add($"A aula persistida {lesson.Id} não possui um caminho relativo seguro.");
            }
        }

        foreach (var topic in course.Modules.SelectMany(module => module.Topics))
        {
            if (LocalCourseStructurePathHelper.TryNormalize(topic.SourceRelativePath, out var topicPath) ||
                TryInferTopicSourceRelativePath(topic, lessonPaths, out topicPath))
            {
                topicPaths[topic] = topicPath;
            }
            else
            {
                diagnostics.Add($"O tópico persistido {topic.Id} não possui um caminho estrutural seguro.");
            }
        }

        foreach (var module in course.Modules)
        {
            if (LocalCourseStructurePathHelper.TryNormalize(module.SourceRelativePath, out var modulePath) ||
                TryInferModuleSourceRelativePath(module, lessonPaths, topicPaths, out modulePath))
            {
                modulePaths[module] = modulePath;
            }
            else
            {
                diagnostics.Add($"O módulo persistido {module.Id} não possui um caminho estrutural seguro.");
            }
        }

        if (diagnostics.Count > 0)
        {
            return new ExistingPreparation(new ExistingStructure([], [], []), diagnostics);
        }

        var modules = new List<ExistingModuleReference>();
        var topics = new List<ExistingTopicReference>();
        var lessons = new List<ExistingLessonReference>();

        foreach (var module in course.Modules)
        {
            var moduleReference = new ExistingModuleReference(module, modulePaths[module]);
            modules.Add(moduleReference);

            foreach (var topic in module.Topics)
            {
                var topicPath = topicPaths[topic];
                if (!LocalCourseStructurePathHelper.TryMakeRelativeToParent(
                        moduleReference.SourceRelativePath,
                        topicPath,
                        out _))
                {
                    diagnostics.Add(
                        $"O tópico persistido {topic.Id} não pertence ao caminho estrutural do módulo {module.Id}.");
                    continue;
                }

                var topicReference = new ExistingTopicReference(topic, moduleReference, topicPath);
                topics.Add(topicReference);

                foreach (var lesson in topic.Lessons)
                {
                    var lessonPath = lessonPaths[lesson];
                    if (!LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                            lessonPath,
                            out var lessonDirectory) ||
                        !PathComparer.Equals(lessonDirectory, topicPath))
                    {
                        diagnostics.Add(
                            $"A aula persistida {lesson.Id} não pertence ao caminho estrutural do tópico {topic.Id}.");
                        continue;
                    }

                    lessons.Add(new ExistingLessonReference(
                        lesson,
                        moduleReference,
                        topicReference,
                        lessonPath));
                }
            }
        }

        return new ExistingPreparation(new ExistingStructure(modules, topics, lessons), diagnostics);
    }

    private static CandidatePreparation PrepareCandidateStructure(DetectedCourseStructure detectedStructure)
    {
        var diagnostics = new List<string>();
        var modules = new List<CandidateModuleReference>();
        var topics = new List<CandidateTopicReference>();
        var lessons = new List<CandidateLessonReference>();

        foreach (var detectedModule in detectedStructure.Modules)
        {
            if (!LocalCourseStructurePathHelper.TryNormalize(
                    detectedModule.RelativePath,
                    out var modulePath))
            {
                diagnostics.Add(
                    $"O módulo detectado \"{detectedModule.RawName}\" possui um caminho estrutural inválido.");
                continue;
            }

            var moduleReference = new CandidateModuleReference(detectedModule, modulePath);
            modules.Add(moduleReference);

            foreach (var detectedTopic in detectedModule.Topics)
            {
                if (!LocalCourseStructurePathHelper.TryCombine(
                        modulePath,
                        detectedTopic.RelativePath,
                        out var topicPath))
                {
                    diagnostics.Add(
                        $"O tópico detectado \"{detectedTopic.RawName}\" possui um caminho estrutural inválido.");
                    continue;
                }

                var topicReference = new CandidateTopicReference(
                    detectedTopic,
                    moduleReference,
                    topicPath);
                topics.Add(topicReference);

                foreach (var detectedLesson in detectedTopic.Lessons)
                {
                    if (!LocalLessonPathHelper.TryNormalizePortableRelativePath(
                            detectedLesson.RelativePath,
                            out var lessonPath) ||
                        !LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                            lessonPath,
                            out var lessonDirectory) ||
                        !PathComparer.Equals(lessonDirectory, topicPath))
                    {
                        diagnostics.Add(
                            $"A aula detectada \"{detectedLesson.FileName}\" possui uma associação estrutural inválida.");
                        continue;
                    }

                    lessons.Add(new CandidateLessonReference(
                        detectedLesson,
                        moduleReference,
                        topicReference,
                        lessonPath));
                }
            }
        }

        return new CandidatePreparation(new CandidateStructure(modules, topics, lessons), diagnostics);
    }

    private static List<string> FindDuplicateKeys(
        ExistingStructure existing,
        CandidateStructure candidate)
    {
        var diagnostics = new List<string>();
        AddDuplicateDiagnostics(
            existing.Modules,
            item => item.SourceRelativePath,
            PathComparer,
            "módulo persistido",
            diagnostics);
        AddDuplicateDiagnostics(
            candidate.Modules,
            item => item.SourceRelativePath,
            PathComparer,
            "módulo detectado",
            diagnostics);
        AddDuplicateDiagnostics(
            existing.Topics,
            item => item.Key,
            TopicKeys,
            "tópico persistido",
            diagnostics);
        AddDuplicateDiagnostics(
            candidate.Topics,
            item => item.Key,
            TopicKeys,
            "tópico detectado",
            diagnostics);
        AddDuplicateDiagnostics(
            existing.Lessons,
            item => item.RelativeFilePath,
            PathComparer,
            "aula persistida",
            diagnostics);
        AddDuplicateDiagnostics(
            candidate.Lessons,
            item => item.RelativeFilePath,
            PathComparer,
            "aula detectada",
            diagnostics);
        AddDuplicateDiagnostics(
            existing.Lessons,
            item => item.Key,
            LessonKeys,
            "associação de aula persistida",
            diagnostics);
        AddDuplicateDiagnostics(
            candidate.Lessons,
            item => item.Key,
            LessonKeys,
            "associação de aula detectada",
            diagnostics);
        return diagnostics;
    }

    private static void AddDuplicateDiagnostics<TItem, TKey>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey> comparer,
        string description,
        List<string> diagnostics)
        where TKey : notnull
    {
        var keys = new HashSet<TKey>(comparer);
        foreach (var item in items)
        {
            if (!keys.Add(keySelector(item)))
            {
                diagnostics.Add($"Foi encontrada uma chave duplicada de {description}.");
            }
        }
    }

    private static CourseContentSyncPreviewResult BuildPlan(
        CourseRecord course,
        DetectedCourseStructure detectedStructure,
        string normalizedRootPath,
        ExistingStructure existing,
        CandidateStructure candidate)
    {
        var existingModules = existing.Modules.ToDictionary(
            item => item.SourceRelativePath,
            PathComparer);
        var candidateModules = candidate.Modules.ToDictionary(
            item => item.SourceRelativePath,
            PathComparer);
        var existingTopics = existing.Topics.ToDictionary(item => item.Key, TopicKeys);
        var candidateTopics = candidate.Topics.ToDictionary(item => item.Key, TopicKeys);
        var existingLessons = existing.Lessons.ToDictionary(item => item.Key, LessonKeys);
        var candidateLessons = candidate.Lessons.ToDictionary(item => item.Key, LessonKeys);

        var modules = BuildModuleItems(existingModules, candidateModules);
        var topics = BuildTopicItems(existingModules, existingTopics, candidateTopics);
        var lessons = BuildLessonItems(existingTopics, existingLessons, candidateLessons);

        SortPlanItems(modules, topics, lessons);

        var result = CreateResult(
            course,
            detectedStructure,
            normalizedRootPath,
            CourseContentSyncPreviewStatus.NotEvaluated,
            string.Empty);
        result.Modules = modules;
        result.Topics = topics;
        result.Lessons = lessons;
        result.Status = result.HasChanges
            ? CourseContentSyncPreviewStatus.Ready
            : CourseContentSyncPreviewStatus.NoChanges;
        result.Message = result.HasChanges
            ? "O plano de sincronização está pronto para revisão."
            : "O conteúdo persistido corresponde à estrutura encontrada na pasta atual.";
        return result;
    }

    private static List<CourseContentSyncModulePlanItem> BuildModuleItems(
        IReadOnlyDictionary<string, ExistingModuleReference> existing,
        IReadOnlyDictionary<string, CandidateModuleReference> candidate)
    {
        var items = new List<CourseContentSyncModulePlanItem>();

        foreach (var existingItem in existing.Values)
        {
            candidate.TryGetValue(existingItem.SourceRelativePath, out var candidateItem);
            items.Add(CreateModuleItem(existingItem, candidateItem));
        }

        foreach (var candidateItem in candidate.Values)
        {
            if (!existing.ContainsKey(candidateItem.SourceRelativePath))
            {
                items.Add(CreateModuleItem(null, candidateItem));
            }
        }

        return items;
    }

    private static List<CourseContentSyncTopicPlanItem> BuildTopicItems(
        IReadOnlyDictionary<string, ExistingModuleReference> existingModules,
        IReadOnlyDictionary<TopicKey, ExistingTopicReference> existing,
        IReadOnlyDictionary<TopicKey, CandidateTopicReference> candidate)
    {
        var items = new List<CourseContentSyncTopicPlanItem>();

        foreach (var existingItem in existing.Values)
        {
            candidate.TryGetValue(existingItem.Key, out var candidateItem);
            items.Add(CreateTopicItem(existingItem, candidateItem, existingItem.Module.Record.Id));
        }

        foreach (var candidateItem in candidate.Values)
        {
            if (existing.ContainsKey(candidateItem.Key))
            {
                continue;
            }

            var existingModuleId = existingModules.TryGetValue(
                candidateItem.Module.SourceRelativePath,
                out var existingModule)
                ? existingModule.Record.Id
                : (Guid?)null;
            items.Add(CreateTopicItem(null, candidateItem, existingModuleId));
        }

        return items;
    }

    private static List<CourseContentSyncLessonPlanItem> BuildLessonItems(
        IReadOnlyDictionary<TopicKey, ExistingTopicReference> existingTopics,
        IReadOnlyDictionary<LessonKey, ExistingLessonReference> existing,
        IReadOnlyDictionary<LessonKey, CandidateLessonReference> candidate)
    {
        var items = new List<CourseContentSyncLessonPlanItem>();

        foreach (var existingItem in existing.Values)
        {
            candidate.TryGetValue(existingItem.Key, out var candidateItem);
            items.Add(CreateLessonItem(existingItem, candidateItem, existingItem.Topic.Record.Id));
        }

        foreach (var candidateItem in candidate.Values)
        {
            if (existing.ContainsKey(candidateItem.Key))
            {
                continue;
            }

            var existingTopicId = existingTopics.TryGetValue(
                candidateItem.Topic.Key,
                out var existingTopic)
                ? existingTopic.Record.Id
                : (Guid?)null;
            items.Add(CreateLessonItem(null, candidateItem, existingTopicId));
        }

        return items;
    }

    private static CourseContentSyncModulePlanItem CreateModuleItem(
        ExistingModuleReference? existing,
        CandidateModuleReference? candidate)
        => new()
        {
            ExistingModuleId = existing?.Record.Id,
            SourceRelativePath = existing?.SourceRelativePath ?? candidate?.SourceRelativePath ?? string.Empty,
            Title = existing is null ? ResolveDetectedName(candidate?.Detected.RawName) : ResolveTitle(existing.Record),
            ExistingTitle = existing is null ? string.Empty : ResolveTitle(existing.Record),
            DetectedName = ResolveDetectedName(candidate?.Detected.RawName),
            ExistingOrder = existing?.Record.Order,
            DetectedOrder = candidate?.Detected.Order,
            ChangeKind = ResolveChangeKind(existing is not null, candidate is not null)
        };

    private static CourseContentSyncTopicPlanItem CreateTopicItem(
        ExistingTopicReference? existing,
        CandidateTopicReference? candidate,
        Guid? existingModuleId)
        => new()
        {
            ExistingTopicId = existing?.Record.Id,
            ExistingModuleId = existingModuleId,
            ModuleSourceRelativePath = existing?.Module.SourceRelativePath ??
                                       candidate?.Module.SourceRelativePath ??
                                       string.Empty,
            SourceRelativePath = existing?.SourceRelativePath ?? candidate?.SourceRelativePath ?? string.Empty,
            Title = existing is null ? ResolveDetectedName(candidate?.Detected.RawName) : ResolveTitle(existing.Record),
            ExistingTitle = existing is null ? string.Empty : ResolveTitle(existing.Record),
            DetectedName = ResolveDetectedName(candidate?.Detected.RawName),
            ExistingOrder = existing?.Record.Order,
            DetectedOrder = candidate?.Detected.Order,
            ChangeKind = ResolveChangeKind(existing is not null, candidate is not null)
        };

    private static CourseContentSyncLessonPlanItem CreateLessonItem(
        ExistingLessonReference? existing,
        CandidateLessonReference? candidate,
        Guid? existingTopicId)
        => new()
        {
            ExistingLessonId = existing?.Record.Id,
            ExistingTopicId = existingTopicId,
            ModuleSourceRelativePath = existing?.Module.SourceRelativePath ??
                                       candidate?.Module.SourceRelativePath ??
                                       string.Empty,
            TopicSourceRelativePath = existing?.Topic.SourceRelativePath ??
                                      candidate?.Topic.SourceRelativePath ??
                                      string.Empty,
            RelativeFilePath = existing?.RelativeFilePath ?? candidate?.RelativeFilePath ?? string.Empty,
            Title = existing is null ? ResolveDetectedLessonName(candidate?.Detected) : ResolveTitle(existing.Record),
            ExistingTitle = existing is null ? string.Empty : ResolveTitle(existing.Record),
            DetectedName = ResolveDetectedLessonName(candidate?.Detected),
            DetectedAbsolutePath = candidate?.Detected.AbsolutePath ?? string.Empty,
            DetectedDuration = candidate?.Detected.Duration,
            DetectedFileSizeBytes = candidate?.Detected.FileSizeBytes,
            ExistingOrder = existing?.Record.Order,
            DetectedOrder = candidate?.Detected.Order,
            ChangeKind = ResolveChangeKind(existing is not null, candidate is not null)
        };

    private static void SortPlanItems(
        List<CourseContentSyncModulePlanItem> modules,
        List<CourseContentSyncTopicPlanItem> topics,
        List<CourseContentSyncLessonPlanItem> lessons)
    {
        var orderedModules = modules
            .OrderBy(EffectiveOrder)
            .ThenBy(item => ChangeSortRank(item.ChangeKind))
            .ThenBy(item => item.SourceRelativePath, PathComparer)
            .ThenBy(item => item.SourceRelativePath, StringComparer.Ordinal)
            .ToList();
        modules.Clear();
        modules.AddRange(orderedModules);

        var moduleRanks = modules
            .Select((item, index) => (item.SourceRelativePath, index))
            .ToDictionary(item => item.SourceRelativePath, item => item.index, PathComparer);
        var orderedTopics = topics
            .OrderBy(item => moduleRanks[item.ModuleSourceRelativePath])
            .ThenBy(EffectiveOrder)
            .ThenBy(item => ChangeSortRank(item.ChangeKind))
            .ThenBy(item => item.SourceRelativePath, PathComparer)
            .ThenBy(item => item.SourceRelativePath, StringComparer.Ordinal)
            .ToList();
        topics.Clear();
        topics.AddRange(orderedTopics);

        var topicRanks = topics
            .Select((item, index) => (Key: new TopicKey(
                item.ModuleSourceRelativePath,
                item.SourceRelativePath), index))
            .ToDictionary(item => item.Key, item => item.index, TopicKeys);
        var orderedLessons = lessons
            .OrderBy(item => moduleRanks[item.ModuleSourceRelativePath])
            .ThenBy(item => topicRanks[new TopicKey(
                item.ModuleSourceRelativePath,
                item.TopicSourceRelativePath)])
            .ThenBy(EffectiveOrder)
            .ThenBy(item => ChangeSortRank(item.ChangeKind))
            .ThenBy(item => item.RelativeFilePath, PathComparer)
            .ThenBy(item => item.RelativeFilePath, StringComparer.Ordinal)
            .ToList();
        lessons.Clear();
        lessons.AddRange(orderedLessons);
    }

    private static int EffectiveOrder(CourseContentSyncModulePlanItem item)
        => item.ExistingOrder ?? item.DetectedOrder ?? int.MaxValue;

    private static int EffectiveOrder(CourseContentSyncTopicPlanItem item)
        => item.ExistingOrder ?? item.DetectedOrder ?? int.MaxValue;

    private static int EffectiveOrder(CourseContentSyncLessonPlanItem item)
        => item.ExistingOrder ?? item.DetectedOrder ?? int.MaxValue;

    private static int ChangeSortRank(CourseContentSyncChangeKind kind)
        => kind switch
        {
            CourseContentSyncChangeKind.Unchanged => 0,
            CourseContentSyncChangeKind.Missing => 1,
            _ => 2
        };

    private static CourseContentSyncChangeKind ResolveChangeKind(bool hasExisting, bool hasCandidate)
        => (hasExisting, hasCandidate) switch
        {
            (true, true) => CourseContentSyncChangeKind.Unchanged,
            (false, true) => CourseContentSyncChangeKind.New,
            _ => CourseContentSyncChangeKind.Missing
        };

    private static bool TryGetComparableLessonRelativePath(
        LessonRecord lesson,
        string rootPath,
        out string relativeFilePath)
        => LocalLessonPathHelper.TryNormalizePortableRelativePath(
               lesson.RelativeFilePath,
               out relativeFilePath) ||
           LocalLessonPathHelper.TryCalculatePortableRelativePath(
               rootPath,
               lesson.LocalFilePath,
               out relativeFilePath) ||
           LocalLessonPathHelper.TryCalculatePortableRelativePath(
               rootPath,
               lesson.FilePath,
               out relativeFilePath);

    private static bool TryInferTopicSourceRelativePath(
        TopicRecord topic,
        IReadOnlyDictionary<LessonRecord, string> lessonPaths,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        var localLessons = topic.Lessons
            .Where(lesson => lesson.SourceType == LessonSourceType.LocalFile)
            .ToList();
        if (localLessons.Count == 0)
        {
            return false;
        }

        string? commonDirectory = null;
        foreach (var lesson in localLessons)
        {
            if (!lessonPaths.TryGetValue(lesson, out var lessonPath) ||
                !LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                    lessonPath,
                    out var lessonDirectory))
            {
                return false;
            }

            if (commonDirectory is null)
            {
                commonDirectory = lessonDirectory;
            }
            else if (!PathComparer.Equals(commonDirectory, lessonDirectory))
            {
                return false;
            }
        }

        return LocalCourseStructurePathHelper.TryNormalize(commonDirectory, out sourceRelativePath);
    }

    private static bool TryInferModuleSourceRelativePath(
        ModuleRecord module,
        IReadOnlyDictionary<LessonRecord, string> lessonPaths,
        IReadOnlyDictionary<TopicRecord, string> topicPaths,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        var directories = new List<string>();
        var localLessons = module.Topics
            .SelectMany(topic => topic.Lessons)
            .Where(lesson => lesson.SourceType == LessonSourceType.LocalFile)
            .ToList();

        foreach (var lesson in localLessons)
        {
            if (!lessonPaths.TryGetValue(lesson, out var lessonPath) ||
                !LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                    lessonPath,
                    out var lessonDirectory))
            {
                return false;
            }

            directories.Add(lessonDirectory);
        }

        if (directories.Count == 0)
        {
            foreach (var topic in module.Topics)
            {
                if (!topicPaths.TryGetValue(topic, out var topicPath))
                {
                    return false;
                }

                directories.Add(topicPath);
            }
        }

        return TryInferModulePathFromDirectories(directories, out sourceRelativePath);
    }

    private static bool TryInferModulePathFromDirectories(
        IReadOnlyCollection<string> directories,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        if (directories.Count == 0)
        {
            return false;
        }

        var normalizedDirectories = new HashSet<string>(PathComparer);
        foreach (var directory in directories)
        {
            if (!LocalCourseStructurePathHelper.TryNormalize(directory, out var normalizedDirectory))
            {
                return false;
            }

            normalizedDirectories.Add(normalizedDirectory);
        }

        if (normalizedDirectories.Contains("."))
        {
            sourceRelativePath = ".";
            return true;
        }

        if (normalizedDirectories.Count == 1)
        {
            var onlyDirectory = normalizedDirectories.Single();
            if (onlyDirectory.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }

            sourceRelativePath = onlyDirectory;
            return true;
        }

        return LocalCourseStructurePathHelper.TryGetCommonAncestor(
                   normalizedDirectories,
                   out var commonAncestor) &&
               commonAncestor != "." &&
               LocalCourseStructurePathHelper.TryNormalize(commonAncestor, out sourceRelativePath);
    }

    private static CourseContentSyncPreviewResult CreateResult(
        CourseRecord course,
        DetectedCourseStructure detectedStructure,
        string normalizedRootPath,
        CourseContentSyncPreviewStatus status,
        string message,
        IEnumerable<string>? diagnostics = null)
        => new()
        {
            CourseId = course.Id,
            CourseTitle = ResolveTitle(course),
            RootPath = normalizedRootPath ?? string.Empty,
            ScannedAtUtc = NormalizeScannedAt(detectedStructure.ScannedAt),
            Status = status,
            Message = message,
            Diagnostics = diagnostics?.ToList() ?? []
        };

    private static DateTime? NormalizeScannedAt(DateTime scannedAt)
    {
        if (scannedAt == default)
        {
            return null;
        }

        return scannedAt.Kind switch
        {
            DateTimeKind.Utc => scannedAt,
            DateTimeKind.Local => scannedAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(scannedAt, DateTimeKind.Utc)
        };
    }

    private static string ResolveTitle(CourseRecord record)
        => string.IsNullOrWhiteSpace(record.Title) ? record.RawTitle : record.Title;

    private static string ResolveTitle(ModuleRecord record)
        => string.IsNullOrWhiteSpace(record.Title) ? record.RawTitle : record.Title;

    private static string ResolveTitle(TopicRecord record)
        => string.IsNullOrWhiteSpace(record.Title) ? record.RawTitle : record.Title;

    private static string ResolveTitle(LessonRecord record)
        => string.IsNullOrWhiteSpace(record.Title) ? record.RawTitle : record.Title;

    private static string ResolveDetectedName(string? rawName)
        => rawName ?? string.Empty;

    private static string ResolveDetectedLessonName(DetectedLessonFile? lesson)
        => lesson is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(lesson.RawName)
                ? lesson.FileName
                : lesson.RawName;

    private sealed record ExistingPreparation(ExistingStructure Structure, List<string> Diagnostics);
    private sealed record CandidatePreparation(CandidateStructure Structure, List<string> Diagnostics);
    private sealed record ExistingStructure(
        List<ExistingModuleReference> Modules,
        List<ExistingTopicReference> Topics,
        List<ExistingLessonReference> Lessons);
    private sealed record CandidateStructure(
        List<CandidateModuleReference> Modules,
        List<CandidateTopicReference> Topics,
        List<CandidateLessonReference> Lessons);
    private sealed record ExistingModuleReference(ModuleRecord Record, string SourceRelativePath);
    private sealed record CandidateModuleReference(DetectedModuleStructure Detected, string SourceRelativePath);
    private sealed record ExistingTopicReference(
        TopicRecord Record,
        ExistingModuleReference Module,
        string SourceRelativePath)
    {
        public TopicKey Key => new(Module.SourceRelativePath, SourceRelativePath);
    }
    private sealed record CandidateTopicReference(
        DetectedTopicStructure Detected,
        CandidateModuleReference Module,
        string SourceRelativePath)
    {
        public TopicKey Key => new(Module.SourceRelativePath, SourceRelativePath);
    }
    private sealed record ExistingLessonReference(
        LessonRecord Record,
        ExistingModuleReference Module,
        ExistingTopicReference Topic,
        string RelativeFilePath)
    {
        public LessonKey Key => new(Module.SourceRelativePath, Topic.SourceRelativePath, RelativeFilePath);
    }
    private sealed record CandidateLessonReference(
        DetectedLessonFile Detected,
        CandidateModuleReference Module,
        CandidateTopicReference Topic,
        string RelativeFilePath)
    {
        public LessonKey Key => new(Module.SourceRelativePath, Topic.SourceRelativePath, RelativeFilePath);
    }

    private readonly record struct TopicKey(string ModuleSourceRelativePath, string SourceRelativePath);
    private readonly record struct LessonKey(
        string ModuleSourceRelativePath,
        string TopicSourceRelativePath,
        string RelativeFilePath);

    private sealed class TopicKeyComparer : IEqualityComparer<TopicKey>
    {
        public bool Equals(TopicKey x, TopicKey y)
            => PathComparer.Equals(x.ModuleSourceRelativePath, y.ModuleSourceRelativePath) &&
               PathComparer.Equals(x.SourceRelativePath, y.SourceRelativePath);

        public int GetHashCode(TopicKey obj)
            => HashCode.Combine(
                PathComparer.GetHashCode(obj.ModuleSourceRelativePath),
                PathComparer.GetHashCode(obj.SourceRelativePath));
    }

    private sealed class LessonKeyComparer : IEqualityComparer<LessonKey>
    {
        public bool Equals(LessonKey x, LessonKey y)
            => PathComparer.Equals(x.ModuleSourceRelativePath, y.ModuleSourceRelativePath) &&
               PathComparer.Equals(x.TopicSourceRelativePath, y.TopicSourceRelativePath) &&
               PathComparer.Equals(x.RelativeFilePath, y.RelativeFilePath);

        public int GetHashCode(LessonKey obj)
            => HashCode.Combine(
                PathComparer.GetHashCode(obj.ModuleSourceRelativePath),
                PathComparer.GetHashCode(obj.TopicSourceRelativePath),
                PathComparer.GetHashCode(obj.RelativeFilePath));
    }
}
