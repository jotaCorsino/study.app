using studyhub.application.Interfaces;
using studyhub.domain.Entities;

namespace studyhub.infrastructure.services;

public sealed class LocalLessonFilePathResolver : ILocalLessonFilePathResolver
{
    public string Resolve(string? courseRootPath, Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        if (LocalLessonPathHelper.TryResolvePhysicalPath(
                courseRootPath,
                lesson.RelativeFilePath,
                out var resolvedFilePath))
        {
            return resolvedFilePath;
        }

        if (!string.IsNullOrWhiteSpace(lesson.LocalFilePath))
        {
            return lesson.LocalFilePath;
        }

        return string.IsNullOrWhiteSpace(lesson.FilePath)
            ? string.Empty
            : lesson.FilePath;
    }
}
