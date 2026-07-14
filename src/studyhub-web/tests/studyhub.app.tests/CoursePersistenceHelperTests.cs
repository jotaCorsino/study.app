using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class CoursePersistenceHelperTests
{
    [Fact]
    public async Task UpsertCourseAsync_PreservesExistingStructuralAndHistoricalStateWhenRehydrating()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var missingCurrentLessonId = Guid.NewGuid();
        var completedAtUtc = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);
        const string existingMetadataJson =
            "{\"rootPath\":\"C:/original\",\"customSentinel\":{\"keep\":true}}";
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "studyhub-course-persistence-helper-tests",
            Guid.NewGuid().ToString("N"));
        var lessonPath = Path.Combine(rootPath, "module-1", "topic-1", "lesson-1.mp4");

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "course",
                RawDescription = string.Empty,
                Title = "Course",
                Description = string.Empty,
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = existingMetadataJson,
                TotalDurationMinutes = 10,
                AddedAt = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc),
                CurrentLessonId = missingCurrentLessonId,
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        RawTitle = "module-1",
                        RawDescription = string.Empty,
                        Title = "Module 1",
                        Description = string.Empty,
                        SourceRelativePath = "module-1",
                        IsAvailable = false,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                RawTitle = "topic-1",
                                RawDescription = string.Empty,
                                Title = "Topic 1",
                                Description = string.Empty,
                                SourceRelativePath = "module-1/topic-1",
                                IsAvailable = false,
                                CompletedAtUtc = completedAtUtc,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonId,
                                        TopicId = topicId,
                                        Order = 1,
                                        RawTitle = "lesson-1",
                                        RawDescription = string.Empty,
                                        Title = "Lesson 1",
                                        Description = string.Empty,
                                        FilePath = lessonPath,
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = lessonPath,
                                        RelativeFilePath = "module-1/topic-1/lesson-1.mp4",
                                        IsAvailable = false,
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 10,
                                        Status = LessonStatus.InProgress,
                                        WatchedPercentage = 42.5,
                                        LastPlaybackPositionSeconds = 123
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });
            await setupContext.SaveChangesAsync();
        }

        var incomingCourse = new Course
        {
            Id = courseId,
            RawTitle = "course",
            RawDescription = string.Empty,
            Title = "Course",
            Description = string.Empty,
            Category = "Curso Local",
            ThumbnailUrl = string.Empty,
            SourceType = CourseSourceType.LocalFolder,
            SourceMetadata = new CourseSourceMetadata
            {
                RootPath = rootPath,
                ImportedAt = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc),
                LastScannedAtUtc = new DateTime(2026, 7, 14, 11, 0, 0, DateTimeKind.Utc)
            },
            TotalDuration = TimeSpan.FromMinutes(10),
            AddedAt = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc),
            Modules =
            [
                new Module
                {
                    Id = moduleId,
                    CourseId = courseId,
                    Order = 1,
                    RawTitle = "module-1",
                    RawDescription = string.Empty,
                    Title = "Module 1",
                    Description = string.Empty,
                    SourceRelativePath = string.Empty,
                    IsAvailable = true,
                    Topics =
                    [
                        new Topic
                        {
                            Id = topicId,
                            ModuleId = moduleId,
                            Order = 1,
                            RawTitle = "topic-1",
                            RawDescription = string.Empty,
                            Title = "Topic 1",
                            Description = string.Empty,
                            SourceRelativePath = "../unsafe-topic",
                            IsAvailable = true,
                            Lessons =
                            [
                                new Lesson
                                {
                                    Id = lessonId,
                                    TopicId = topicId,
                                    Order = 1,
                                    RawTitle = "lesson-1",
                                    RawDescription = string.Empty,
                                    Title = "Lesson 1",
                                    Description = string.Empty,
                                    SourceType = LessonSourceType.LocalFile,
                                    LocalFilePath = lessonPath,
                                    RelativeFilePath = "module-1/topic-1/lesson-1.mp4",
                                    IsAvailable = true,
                                    Provider = "LocalFileSystem",
                                    Duration = TimeSpan.FromMinutes(10)
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        await using (var updateContext = new StudyHubDbContext(options))
        {
            await CoursePersistenceHelper.UpsertCourseAsync(updateContext, incomingCourse);
        }

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync(course => course.Id == courseId);
        var persistedModule = Assert.Single(persistedCourse.Modules);
        var persistedTopic = Assert.Single(persistedModule.Topics);
        var persistedLesson = Assert.Single(persistedTopic.Lessons);

        Assert.Equal(moduleId, persistedModule.Id);
        Assert.Equal(topicId, persistedTopic.Id);
        Assert.Equal(lessonId, persistedLesson.Id);
        Assert.Equal("module-1", persistedModule.SourceRelativePath);
        Assert.Equal("module-1/topic-1", persistedTopic.SourceRelativePath);
        Assert.Equal(missingCurrentLessonId, persistedCourse.CurrentLessonId);
        Assert.Equal(existingMetadataJson, persistedCourse.SourceMetadataJson);
        Assert.False(persistedModule.IsAvailable);
        Assert.False(persistedTopic.IsAvailable);
        Assert.False(persistedLesson.IsAvailable);
        Assert.Equal(completedAtUtc, persistedTopic.CompletedAtUtc);
        Assert.Equal(LessonStatus.InProgress, persistedLesson.Status);
        Assert.Equal(42.5, persistedLesson.WatchedPercentage);
        Assert.Equal(123, persistedLesson.LastPlaybackPositionSeconds);
    }
}
