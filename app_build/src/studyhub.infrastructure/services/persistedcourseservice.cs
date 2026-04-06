using Microsoft.EntityFrameworkCore;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public class PersistedCourseService(IDbContextFactory<StudyHubDbContext> contextFactory) : ICourseService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;

    public async Task<List<Course>> GetAllCoursesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var records = await BuildCourseQuery(context)
            .OrderBy(record => record.AddedAt)
            .ToListAsync();

        return records.Select(record => record.ToDomain()).ToList();
    }

    public async Task<Course?> GetCourseByIdAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await BuildCourseQuery(context)
            .FirstOrDefaultAsync(course => course.Id == id);

        return record?.ToDomain();
    }

    public async Task<Lesson?> GetLessonByIdAsync(Guid courseId, Guid lessonId)
    {
        var course = await GetCourseByIdAsync(courseId);

        return course?.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .FirstOrDefault(lesson => lesson.Id == lessonId);
    }

    public async Task<Lesson?> GetNextLessonAsync(Guid courseId, Guid currentLessonId)
    {
        var lessons = await GetOrderedLessonsAsync(courseId);
        var index = lessons.FindIndex(lesson => lesson.Id == currentLessonId);

        return index >= 0 && index < lessons.Count - 1 ? lessons[index + 1] : null;
    }

    public async Task<Lesson?> GetPreviousLessonAsync(Guid courseId, Guid currentLessonId)
    {
        var lessons = await GetOrderedLessonsAsync(courseId);
        var index = lessons.FindIndex(lesson => lesson.Id == currentLessonId);

        return index > 0 ? lessons[index - 1] : null;
    }

    public async Task DeleteCourseAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await context.Courses.FirstOrDefaultAsync(course => course.Id == id);
        if (record == null)
        {
            return;
        }

        context.Courses.Remove(record);
        await context.SaveChangesAsync();
    }

    private async Task<List<Lesson>> GetOrderedLessonsAsync(Guid courseId)
    {
        var course = await GetCourseByIdAsync(courseId);

        return course?.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList() ?? [];
    }

    private static IQueryable<persistence.models.CourseRecord> BuildCourseQuery(StudyHubDbContext context)
    {
        return context.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons);
    }
}
