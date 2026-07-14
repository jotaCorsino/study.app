using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface ILocalLessonFilePathResolver
{
    string Resolve(string? courseRootPath, Lesson lesson);
}
