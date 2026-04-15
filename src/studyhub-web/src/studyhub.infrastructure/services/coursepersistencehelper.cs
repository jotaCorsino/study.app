using Microsoft.EntityFrameworkCore;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.shared.Enums;

namespace studyhub.infrastructure.services;

internal static class CoursePersistenceHelper
{
    public static async Task UpsertCourseAsync(StudyHubDbContext context, Course course, CancellationToken cancellationToken = default)
    {
        var record = course.ToRecord();
        var existingCourse = await context.Courses
            .Include(item => item.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .FirstOrDefaultAsync(item => item.Id == record.Id, cancellationToken);

        if (existingCourse == null)
        {
            await context.Courses.AddAsync(record, cancellationToken);
            await SaveAncillaryRecordsAsync(context, record.Id, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var existingLessonStateById = existingCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToDictionary(
                lesson => lesson.Id,
                lesson => new PreservedLessonState
                {
                    Status = lesson.Status,
                    WatchedPercentage = lesson.WatchedPercentage,
                    LastPlaybackPositionSeconds = lesson.LastPlaybackPositionSeconds,
                    DurationMinutes = lesson.DurationMinutes
                });

        ApplyPreservedLessonState(record, existingLessonStateById);

        var preservedCurrentLessonId = existingCourse.CurrentLessonId is Guid currentLessonId &&
                                       record.Modules
                                           .SelectMany(module => module.Topics)
                                           .SelectMany(topic => topic.Lessons)
                                           .Any(lesson => lesson.Id == currentLessonId)
            ? currentLessonId
            : (Guid?)null;

        var existingModules = existingCourse.Modules.ToList();
        if (existingModules.Count > 0)
        {
            context.Modules.RemoveRange(existingModules);
            await context.SaveChangesAsync(cancellationToken);
        }

        existingCourse.Modules.Clear();
        existingCourse.RawTitle = record.RawTitle;
        existingCourse.RawDescription = record.RawDescription;
        existingCourse.Title = record.Title;
        existingCourse.Description = record.Description;
        existingCourse.Category = record.Category;
        existingCourse.ThumbnailUrl = record.ThumbnailUrl;
        existingCourse.FolderPath = record.FolderPath;
        existingCourse.SourceType = record.SourceType;
        existingCourse.SourceMetadataJson = record.SourceMetadataJson;
        existingCourse.TotalDurationMinutes = record.TotalDurationMinutes;
        existingCourse.AddedAt = existingCourse.AddedAt == default
            ? record.AddedAt
            : existingCourse.AddedAt;
        existingCourse.LastAccessedAt = record.LastAccessedAt ?? existingCourse.LastAccessedAt;
        existingCourse.CurrentLessonId = preservedCurrentLessonId ?? record.CurrentLessonId;

        foreach (var module in record.Modules)
        {
            existingCourse.Modules.Add(module);
        }

        await SaveAncillaryRecordsAsync(context, record.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SaveAncillaryRecordsAsync(StudyHubDbContext context, Guid courseId, CancellationToken cancellationToken = default)
    {
        if (!await context.CourseRoadmaps.AnyAsync(item => item.CourseId == courseId, cancellationToken))
        {
            await context.CourseRoadmaps.AddAsync(new CourseRoadmapRecord
            {
                CourseId = courseId,
                LevelsJson = PersistenceMapper.SerializeRoadmap([]),
                UpdatedAt = DateTime.Now
            }, cancellationToken);
        }

        if (!await context.CourseMaterials.AnyAsync(item => item.CourseId == courseId, cancellationToken))
        {
            await context.CourseMaterials.AddAsync(new CourseMaterialsRecord
            {
                CourseId = courseId,
                MaterialsJson = PersistenceMapper.SerializeMaterials([]),
                UpdatedAt = DateTime.Now
            }, cancellationToken);
        }
    }

    private static void ApplyPreservedLessonState(
        CourseRecord record,
        IReadOnlyDictionary<Guid, PreservedLessonState> existingLessonStateById)
    {
        foreach (var lesson in record.Modules
                     .SelectMany(module => module.Topics)
                     .SelectMany(topic => topic.Lessons))
        {
            if (!existingLessonStateById.TryGetValue(lesson.Id, out var preservedState))
            {
                continue;
            }

            lesson.Status = preservedState.Status;
            lesson.WatchedPercentage = preservedState.WatchedPercentage;
            lesson.LastPlaybackPositionSeconds = preservedState.LastPlaybackPositionSeconds;

            if (lesson.DurationMinutes <= 0 && preservedState.DurationMinutes > 0)
            {
                lesson.DurationMinutes = preservedState.DurationMinutes;
            }
        }
    }

    private sealed class PreservedLessonState
    {
        public LessonStatus Status { get; set; }
        public double WatchedPercentage { get; set; }
        public int LastPlaybackPositionSeconds { get; set; }
        public int DurationMinutes { get; set; }
    }
}
