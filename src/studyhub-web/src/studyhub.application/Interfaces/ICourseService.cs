using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface ICourseService
{
    Task<List<Course>> GetAllCoursesAsync();
    Task<Course?> GetCourseByIdAsync(Guid id);
    Task<CourseSourceMetadata?> UpdateCourseIntroSkipPreferenceAsync(Guid id, bool introSkipEnabled, int introSkipSeconds);
    Task<Lesson?> GetLessonByIdAsync(Guid courseId, Guid lessonId);
    Task<Lesson?> GetNextLessonAsync(Guid courseId, Guid currentLessonId);
    Task<Lesson?> GetPreviousLessonAsync(Guid courseId, Guid currentLessonId);
    Task DeleteCourseAsync(Guid id);
}
