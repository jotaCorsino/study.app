using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface IProgressService
{
    Task<Progress?> GetProgressByCourseAsync(Guid courseId);
    Task<List<Progress>> GetAllProgressAsync();
    Task OpenLessonAsync(Guid courseId, Guid lessonId);
    Task MarkLessonCompletedAsync(Guid courseId, Guid lessonId);
    Task UpdateLessonProgressAsync(Guid courseId, Guid lessonId, double watchedPercentage);
    Task UpdateLessonPlaybackAsync(Guid courseId, Guid lessonId, TimeSpan currentPosition, TimeSpan totalDuration, bool markAsCompleted = false);
}
