using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface ICourseResumeService
{
    Task<Lesson?> ResolveResumeLessonAsync(Guid courseId);
}
