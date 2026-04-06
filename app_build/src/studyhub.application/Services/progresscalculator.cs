using studyhub.domain.Entities;
using studyhub.shared.Enums;

namespace studyhub.application.Services;

public static class ProgressCalculator
{
    public static Progress Calculate(Course course)
    {
        var allLessons = course.Modules
            .OrderBy(m => m.Order)
            .SelectMany(m => m.Topics.OrderBy(t => t.Order))
            .SelectMany(t => t.Lessons.OrderBy(l => l.Order))
            .ToList();

        var completedLessons = allLessons.Count(lesson => lesson.Status == LessonStatus.Completed);
        var inProgressLessons = allLessons.Count(lesson => lesson.Status == LessonStatus.InProgress);
        var totalLessons = allLessons.Count;

        var lastLesson = allLessons
            .FirstOrDefault(lesson => lesson.Status == LessonStatus.InProgress)
            ?? allLessons.LastOrDefault(lesson => lesson.Status == LessonStatus.Completed);

        return new Progress
        {
            CourseId = course.Id,
            TotalLessons = totalLessons,
            CompletedLessons = completedLessons,
            InProgressLessons = inProgressLessons,
            OverallPercentage = totalLessons > 0 ? Math.Round(completedLessons / (double)totalLessons * 100, 1) : 0,
            TotalWatchTime = TimeSpan.FromMinutes(allLessons
                .Where(lesson => lesson.Status == LessonStatus.Completed)
                .Sum(lesson => lesson.Duration.TotalMinutes) +
                allLessons
                    .Where(lesson => lesson.Status == LessonStatus.InProgress)
                    .Sum(lesson => lesson.Duration.TotalMinutes * lesson.WatchedPercentage / 100)),
            LastStudiedAt = course.LastAccessedAt,
            CurrentStreak = 0,
            LastLessonId = lastLesson?.Id,
            LastLessonTitle = lastLesson?.Title
        };
    }
}
