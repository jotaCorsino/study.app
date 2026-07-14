using studyhub.application.Contracts.LocalImport;

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
                module.Topics is not { Count: > 0 })
            {
                return false;
            }

            foreach (var topic in module.Topics)
            {
                if (topic is null ||
                    topic.TopicId == Guid.Empty ||
                    !topicIds.Add(topic.TopicId) ||
                    topic.Lessons is not { Count: > 0 })
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
}
