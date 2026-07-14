using studyhub.application.Contracts.LocalImport;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

internal static class LocalCourseManifestValidator
{
    public static bool HasUsableStructure(DetectedCourseStructure? manifest)
    {
        if (manifest is null ||
            manifest.CourseId == Guid.Empty ||
            manifest.Modules is not { Count: > 0 })
        {
            return false;
        }

        var moduleIds = new HashSet<Guid>();
        var topicIds = new HashSet<Guid>();
        var lessonIds = new HashSet<Guid>();

        foreach (var module in manifest.Modules)
        {
            if (module is null ||
                module.ModuleId == Guid.Empty ||
                !moduleIds.Add(module.ModuleId) ||
                module.Topics is null)
            {
                return false;
            }

            foreach (var topic in module.Topics)
            {
                if (topic is null ||
                    topic.TopicId == Guid.Empty ||
                    !topicIds.Add(topic.TopicId) ||
                    topic.Lessons is null)
                {
                    return false;
                }

                foreach (var lesson in topic.Lessons)
                {
                    if (lesson is null ||
                        lesson.LessonId == Guid.Empty ||
                        !lessonIds.Add(lesson.LessonId) ||
                        string.IsNullOrWhiteSpace(lesson.RelativePath))
                    {
                        return false;
                    }
                }
            }
        }

        return lessonIds.Count > 0;
    }

    public static bool HasMatchingPersistedIdentities(
        DetectedCourseStructure? manifest,
        CourseRecord? course)
        => TryCorrelatePersistedIdentities(manifest, course, out var correlation) &&
           correlation.IsExactMatch;

    public static bool TryCorrelatePersistedIdentities(
        DetectedCourseStructure? manifest,
        CourseRecord? course,
        out LocalCourseManifestIdentityCorrelation correlation)
    {
        correlation = LocalCourseManifestIdentityCorrelation.Empty;

        if (!HasUsableStructure(manifest) ||
            course is null ||
            manifest!.CourseId != course.Id)
        {
            return false;
        }

        var manifestModuleIds = new HashSet<Guid>();
        var manifestTopicParents = new Dictionary<Guid, Guid>();
        var manifestLessonParents = new Dictionary<Guid, Guid>();

        foreach (var module in manifest.Modules)
        {
            manifestModuleIds.Add(module.ModuleId);

            foreach (var topic in module.Topics)
            {
                manifestTopicParents.Add(topic.TopicId, module.ModuleId);

                foreach (var lesson in topic.Lessons)
                {
                    manifestLessonParents.Add(lesson.LessonId, topic.TopicId);
                }
            }
        }

        var persistedModuleIds = new HashSet<Guid>();
        var persistedTopicParents = new Dictionary<Guid, Guid>();
        var persistedLessonParents = new Dictionary<Guid, Guid>();

        foreach (var module in course.Modules)
        {
            if (!persistedModuleIds.Add(module.Id))
            {
                return false;
            }

            foreach (var topic in module.Topics)
            {
                if (!persistedTopicParents.TryAdd(topic.Id, module.Id))
                {
                    return false;
                }

                foreach (var lesson in topic.Lessons)
                {
                    if (!persistedLessonParents.TryAdd(lesson.Id, topic.Id))
                    {
                        return false;
                    }
                }
            }
        }

        correlation = new LocalCourseManifestIdentityCorrelation(
            manifestModuleIds,
            manifestTopicParents,
            manifestLessonParents,
            persistedModuleIds,
            persistedTopicParents,
            persistedLessonParents);

        return true;
    }
}

internal sealed class LocalCourseManifestIdentityCorrelation(
    IReadOnlySet<Guid> manifestModuleIds,
    IReadOnlyDictionary<Guid, Guid> manifestTopicParents,
    IReadOnlyDictionary<Guid, Guid> manifestLessonParents,
    IReadOnlySet<Guid> persistedModuleIds,
    IReadOnlyDictionary<Guid, Guid> persistedTopicParents,
    IReadOnlyDictionary<Guid, Guid> persistedLessonParents,
    bool isValid = true)
{
    public static LocalCourseManifestIdentityCorrelation Empty { get; } = new(
        new HashSet<Guid>(),
        new Dictionary<Guid, Guid>(),
        new Dictionary<Guid, Guid>(),
        new HashSet<Guid>(),
        new Dictionary<Guid, Guid>(),
        new Dictionary<Guid, Guid>(),
        isValid: false);

    public bool IsExactMatch =>
        isValid &&
        manifestModuleIds.SetEquals(persistedModuleIds) &&
        HaveSameParents(manifestTopicParents, persistedTopicParents) &&
        HaveSameParents(manifestLessonParents, persistedLessonParents);

    public bool ManifestContainsPersistedTree =>
        isValid &&
        persistedModuleIds.IsSubsetOf(manifestModuleIds) &&
        IsParentMapSubsetOf(persistedTopicParents, manifestTopicParents) &&
        IsParentMapSubsetOf(persistedLessonParents, manifestLessonParents);

    public bool MatchesModule(Guid moduleId)
        => manifestModuleIds.Contains(moduleId) && persistedModuleIds.Contains(moduleId);

    public bool MatchesTopic(Guid moduleId, Guid topicId)
        => MatchesModule(moduleId) &&
           manifestTopicParents.TryGetValue(topicId, out var manifestModuleId) &&
           persistedTopicParents.TryGetValue(topicId, out var persistedModuleId) &&
           manifestModuleId == moduleId &&
           persistedModuleId == moduleId;

    public bool MatchesLesson(Guid topicId, Guid lessonId)
        => manifestLessonParents.TryGetValue(lessonId, out var manifestTopicId) &&
           persistedLessonParents.TryGetValue(lessonId, out var persistedTopicId) &&
           manifestTopicId == topicId &&
           persistedTopicId == topicId;

    private static bool HaveSameParents(
        IReadOnlyDictionary<Guid, Guid> first,
        IReadOnlyDictionary<Guid, Guid> second)
        => first.Count == second.Count &&
           first.All(item => second.TryGetValue(item.Key, out var parentId) && parentId == item.Value);

    private static bool IsParentMapSubsetOf(
        IReadOnlyDictionary<Guid, Guid> subset,
        IReadOnlyDictionary<Guid, Guid> superset)
        => subset.All(item =>
            superset.TryGetValue(item.Key, out var parentId) &&
            parentId == item.Value);
}
