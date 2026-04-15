using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.shared.Enums;

namespace studyhub.infrastructure.services;

public sealed class CourseResumeService(
    ICourseService courseService,
    IProgressService progressService) : ICourseResumeService
{
    private readonly ICourseService _courseService = courseService;
    private readonly IProgressService _progressService = progressService;

    public async Task<Lesson?> ResolveResumeLessonAsync(Guid courseId)
    {
        var course = await _courseService.GetCourseByIdAsync(courseId);
        var orderedLessons = course?.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList() ?? [];

        if (orderedLessons.Count == 0)
        {
            return null;
        }

        var progress = await _progressService.GetProgressByCourseAsync(courseId);
        if (progress?.LastLessonId is Guid lastLessonId)
        {
            var lastLessonIndex = orderedLessons.FindIndex(lesson => lesson.Id == lastLessonId);
            if (lastLessonIndex >= 0)
            {
                var lastRelevantLesson = orderedLessons[lastLessonIndex];
                if (lastRelevantLesson.Status != LessonStatus.Completed)
                {
                    return lastRelevantLesson;
                }

                return lastLessonIndex < orderedLessons.Count - 1
                    ? orderedLessons[lastLessonIndex + 1]
                    : lastRelevantLesson;
            }
        }

        var inProgressLesson = orderedLessons.FirstOrDefault(lesson => lesson.Status == LessonStatus.InProgress);
        if (inProgressLesson != null)
        {
            return inProgressLesson;
        }

        var lastCompletedIndex = orderedLessons.FindLastIndex(lesson => lesson.Status == LessonStatus.Completed);
        if (lastCompletedIndex >= 0)
        {
            return lastCompletedIndex < orderedLessons.Count - 1
                ? orderedLessons[lastCompletedIndex + 1]
                : orderedLessons[lastCompletedIndex];
        }

        return orderedLessons[0];
    }
}
