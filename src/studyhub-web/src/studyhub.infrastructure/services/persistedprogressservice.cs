using Microsoft.EntityFrameworkCore;
using studyhub.application.Interfaces;
using studyhub.application.Services;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.shared.Enums;

namespace studyhub.infrastructure.services;

public class PersistedProgressService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IRoutineService routineService) : IProgressService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IRoutineService _routineService = routineService;
    public event Action<Guid>? DailyProgressChanged;

    public async Task<Progress?> GetProgressByCourseAsync(Guid courseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var record = await BuildCourseQuery(context)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (record == null)
        {
            return null;
        }

        var progress = BuildProgress(record);
        progress.CurrentStreak = await _routineService.GetCurrentStreakAsync(courseId);
        return progress;
    }

    public async Task<List<Progress>> GetAllProgressAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var records = await BuildCourseQuery(context)
            .OrderBy(course => course.AddedAt)
            .ToListAsync();

        var progressEntries = new List<Progress>(records.Count);
        foreach (var record in records)
        {
            var progress = BuildProgress(record);
            progress.CurrentStreak = await _routineService.GetCurrentStreakAsync(record.Id);
            progressEntries.Add(progress);
        }

        return progressEntries;
    }

    public async Task OpenLessonAsync(Guid courseId, Guid lessonId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var course = await context.Courses.FirstOrDefaultAsync(item => item.Id == courseId);
        if (course == null)
        {
            return;
        }

        course.CurrentLessonId = lessonId;
        course.LastAccessedAt = DateTime.Now;

        await context.SaveChangesAsync();
    }

    public async Task MarkLessonCompletedAsync(Guid courseId, Guid lessonId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var lesson = await GetLessonRecordAsync(context, courseId, lessonId);
        if (lesson?.Topic?.Module?.Course == null)
        {
            return;
        }

        var previousCreditableMinutes = ResolveCreditableMinutes(lesson);
        lesson.Status = LessonStatus.Completed;
        lesson.WatchedPercentage = 100;
        lesson.LastPlaybackPositionSeconds = lesson.DurationMinutes > 0
            ? (int)Math.Round(TimeSpan.FromMinutes(lesson.DurationMinutes).TotalSeconds)
            : lesson.LastPlaybackPositionSeconds;
        lesson.Topic.Module.Course.LastAccessedAt = DateTime.Now;
        lesson.Topic.Module.Course.CurrentLessonId = lesson.Id;

        await context.SaveChangesAsync();

        var currentCreditableMinutes = ResolveCreditableMinutes(lesson);
        await ReconcileRoutineCreditAsync(
            courseId,
            lesson,
            currentCreditableMinutes - previousCreditableMinutes);
    }

    public async Task UpdateLessonProgressAsync(Guid courseId, Guid lessonId, double watchedPercentage)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var lesson = await GetLessonRecordAsync(context, courseId, lessonId);
        if (lesson?.Topic?.Module?.Course == null)
        {
            return;
        }

        var normalizedPercentage = Math.Clamp(watchedPercentage, 0, 100);
        var previousCreditableMinutes = ResolveCreditableMinutes(lesson);

        if (normalizedPercentage >= 100)
        {
            lesson.Status = LessonStatus.Completed;
            lesson.WatchedPercentage = 100;
        }
        else if (normalizedPercentage > 0 && lesson.Status != LessonStatus.Completed)
        {
            lesson.Status = LessonStatus.InProgress;
            lesson.WatchedPercentage = normalizedPercentage;
        }

        lesson.Topic.Module.Course.LastAccessedAt = DateTime.Now;
        lesson.Topic.Module.Course.CurrentLessonId = lesson.Id;

        await context.SaveChangesAsync();

        var currentCreditableMinutes = ResolveCreditableMinutes(lesson);
        if (currentCreditableMinutes > previousCreditableMinutes)
        {
            await ReconcileRoutineCreditAsync(
                courseId,
                lesson,
                currentCreditableMinutes - previousCreditableMinutes);
        }
    }

    public async Task UpdateLessonPlaybackAsync(Guid courseId, Guid lessonId, TimeSpan currentPosition, TimeSpan totalDuration, bool markAsCompleted = false)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var lesson = await GetLessonRecordAsync(context, courseId, lessonId);
        if (lesson?.Topic?.Module?.Course == null)
        {
            return;
        }

        var previousCreditableMinutes = ResolveCreditableMinutes(lesson);

        var resolvedDuration = totalDuration > TimeSpan.Zero
            ? totalDuration
            : TimeSpan.FromMinutes(Math.Max(lesson.DurationMinutes, 0));

        var resolvedPosition = currentPosition < TimeSpan.Zero ? TimeSpan.Zero : currentPosition;
        if (resolvedDuration > TimeSpan.Zero && resolvedPosition > resolvedDuration)
        {
            resolvedPosition = resolvedDuration;
        }

        if (resolvedDuration > TimeSpan.Zero && lesson.DurationMinutes == 0)
        {
            lesson.DurationMinutes = Math.Max(1, (int)Math.Round(resolvedDuration.TotalMinutes));
        }

        lesson.LastPlaybackPositionSeconds = Math.Max(0, (int)Math.Round(resolvedPosition.TotalSeconds));

        var watchedPercentage = resolvedDuration > TimeSpan.Zero
            ? Math.Clamp(resolvedPosition.TotalSeconds / resolvedDuration.TotalSeconds * 100, 0, 100)
            : lesson.WatchedPercentage;

        if (markAsCompleted || watchedPercentage >= 98)
        {
            lesson.Status = LessonStatus.Completed;
            lesson.WatchedPercentage = 100;
        }
        else if (watchedPercentage > 0)
        {
            lesson.Status = LessonStatus.InProgress;
            lesson.WatchedPercentage = watchedPercentage;
        }

        lesson.Topic.Module.Course.LastAccessedAt = DateTime.Now;
        lesson.Topic.Module.Course.CurrentLessonId = lesson.Id;
        lesson.Topic.Module.Course.TotalDurationMinutes = await context.Lessons
            .Where(item =>
                item.Topic != null &&
                item.Topic.Module != null &&
                item.Topic.Module.CourseId == courseId)
            .SumAsync(item => item.DurationMinutes);

        await context.SaveChangesAsync();

        var currentCreditableMinutes = ResolveCreditableMinutes(lesson);
        if (currentCreditableMinutes > previousCreditableMinutes)
        {
            await ReconcileRoutineCreditAsync(
                courseId,
                lesson,
                currentCreditableMinutes - previousCreditableMinutes);
        }
    }

    private static Progress BuildProgress(CourseRecord courseRecord)
    {
        var progress = ProgressCalculator.Calculate(courseRecord.ToDomain());
        var currentLesson = courseRecord.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .FirstOrDefault(lesson => lesson.Id == courseRecord.CurrentLessonId);

        if (currentLesson != null)
        {
            progress.LastLessonId = currentLesson.Id;
            progress.LastLessonTitle = currentLesson.Title;
        }

        return progress;
    }

    private static IQueryable<CourseRecord> BuildCourseQuery(StudyHubDbContext context)
    {
        return context.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons);
    }

    private static Task<LessonRecord?> GetLessonRecordAsync(StudyHubDbContext context, Guid courseId, Guid lessonId)
    {
        return context.Lessons
            .Include(lesson => lesson.Topic!)
                .ThenInclude(topic => topic.Module!)
                    .ThenInclude(module => module.Course)
            .FirstOrDefaultAsync(lesson =>
                lesson.Id == lessonId &&
                lesson.Topic != null &&
                lesson.Topic.Module != null &&
                lesson.Topic.Module.CourseId == courseId);
    }

    private async Task ReconcileRoutineCreditAsync(
        Guid courseId,
        LessonRecord lesson,
        int creditedMinutes)
    {
        var totalLessonMinutes = ResolveLessonDurationMinutes(lesson);
        if (totalLessonMinutes <= 0)
        {
            return;
        }

        var normalizedCreditedMinutes = Math.Clamp(creditedMinutes, 0, totalLessonMinutes);
        if (normalizedCreditedMinutes <= 0)
        {
            return;
        }

        await _routineService.CreditLessonProgressAsync(courseId, lesson.Id, normalizedCreditedMinutes);
        NotifyDailyProgressChanged(courseId);
    }

    private static int ResolveCreditableMinutes(LessonRecord lesson)
    {
        var totalLessonMinutes = ResolveLessonDurationMinutes(lesson);
        if (totalLessonMinutes <= 0)
        {
            return 0;
        }

        var byPercentage = (int)Math.Floor(totalLessonMinutes * Math.Clamp(lesson.WatchedPercentage, 0, 100) / 100d);
        var byPlayback = lesson.LastPlaybackPositionSeconds > 0
            ? (int)Math.Floor(TimeSpan.FromSeconds(lesson.LastPlaybackPositionSeconds).TotalMinutes)
            : 0;

        if (lesson.Status == LessonStatus.Completed)
        {
            return totalLessonMinutes;
        }

        return Math.Clamp(Math.Max(byPercentage, byPlayback), 0, totalLessonMinutes);
    }

    private static int ResolveLessonDurationMinutes(LessonRecord lesson)
    {
        if (lesson.DurationMinutes > 0)
        {
            return lesson.DurationMinutes;
        }

        if (lesson.LastPlaybackPositionSeconds > 0)
        {
            return Math.Max(1, (int)Math.Ceiling(TimeSpan.FromSeconds(lesson.LastPlaybackPositionSeconds).TotalMinutes));
        }

        return 0;
    }

    private void NotifyDailyProgressChanged(Guid courseId)
    {
        if (courseId == Guid.Empty)
        {
            return;
        }

        DailyProgressChanged?.Invoke(courseId);
    }
}
