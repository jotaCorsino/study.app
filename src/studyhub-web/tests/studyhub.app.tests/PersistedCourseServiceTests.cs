using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class PersistedCourseServiceTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetCourseByIdAsync_DefaultsCourseLifecycleStatusToActive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, @"C:\courses\active-default", "{}"));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        Assert.Equal(CourseLifecycleStatus.Active, loadedCourse!.LifecycleStatus);
    }

    [Fact]
    public async Task UpdateCourseLifecycleStatusAsync_PersistsStatusAcrossReads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, @"C:\courses\paused-course", "{}"));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var updatedCourse = await service.UpdateCourseLifecycleStatusAsync(courseId, CourseLifecycleStatus.Paused);
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(updatedCourse);
        Assert.NotNull(loadedCourse);
        Assert.Equal(CourseLifecycleStatus.Paused, updatedCourse!.LifecycleStatus);
        Assert.Equal(CourseLifecycleStatus.Paused, loadedCourse!.LifecycleStatus);
    }

    [Fact]
    public async Task DatabaseInitializer_AddsLifecycleStatusColumnToLegacyDatabaseAsActive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var storageRoot = Path.Combine(Path.GetTempPath(), "studyhub-course-status-tests", Guid.NewGuid().ToString("N"));

        try
        {
            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(CreateCourseRecord(courseId, @"C:\courses\legacy-status", "{}"));
                await setupContext.SaveChangesAsync();
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE courses DROP COLUMN lifecycle_status;");
            }

            var initializer = new StudyHubDatabaseInitializer(
                new TestDbContextFactory(options),
                new TestStoragePathsService(storageRoot),
                NullLogger<StudyHubDatabaseInitializer>.Instance);

            await initializer.InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var loadedCourse = await assertContext.Courses.SingleAsync(course => course.Id == courseId);

            Assert.Equal(CourseLifecycleStatus.Active, loadedCourse.LifecycleStatus);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateCourseLifecycleStatusAsync_DoesNotChangeLessonProgress()
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
        var rootPath = @"C:\courses\progress-safe";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "progress-safe",
                RawDescription = "curso local",
                Title = "Progress Safe",
                Description = "Curso local",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 10,
                AddedAt = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc),
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        RawTitle = "modulo",
                        RawDescription = string.Empty,
                        Title = "Modulo",
                        Description = string.Empty,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                RawTitle = "topico",
                                RawDescription = string.Empty,
                                Title = "Topico",
                                Description = string.Empty,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonId,
                                        TopicId = topicId,
                                        Order = 1,
                                        RawTitle = "lesson",
                                        RawDescription = string.Empty,
                                        Title = "Lesson",
                                        Description = string.Empty,
                                        FilePath = $@"{rootPath}\lesson.mp4",
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = $@"{rootPath}\lesson.mp4",
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 10,
                                        Status = LessonStatus.InProgress,
                                        WatchedPercentage = 40,
                                        LastPlaybackPositionSeconds = 240
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        await service.UpdateCourseLifecycleStatusAsync(courseId, CourseLifecycleStatus.Completed);

        await using var assertContext = new StudyHubDbContext(options);
        var lesson = await assertContext.Lessons.SingleAsync(item => item.Id == lessonId);
        var course = await assertContext.Courses.SingleAsync(item => item.Id == courseId);

        Assert.Equal(CourseLifecycleStatus.Completed, course.LifecycleStatus);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(40d, lesson.WatchedPercentage);
        Assert.Equal(240, lesson.LastPlaybackPositionSeconds);
    }

    [Fact]
    public async Task UpdateCourseDetailsAsync_PersistsTrimmedTitleAndDescriptionAcrossReads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\details-update";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, rootPath, "{}"));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var updatedCourse = await service.UpdateCourseDetailsAsync(
            courseId,
            "  Nome editado  ",
            "  Descrição editada  ");

        var readerService = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await readerService.GetCourseByIdAsync(courseId);

        Assert.NotNull(updatedCourse);
        Assert.NotNull(loadedCourse);
        Assert.Equal("Nome editado", updatedCourse!.Title);
        Assert.Equal("Descrição editada", updatedCourse.Description);
        Assert.Equal("Nome editado", loadedCourse!.Title);
        Assert.Equal("Descrição editada", loadedCourse.Description);

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses.SingleAsync(course => course.Id == courseId);
        Assert.Equal($"course-{courseId:N}", persistedCourse.RawTitle);
        Assert.Equal("curso para teste de metadata", persistedCourse.RawDescription);
    }

    [Fact]
    public async Task UpdateCourseDetailsAsync_DoesNotChangeCourse_WhenTitleIsInvalid()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\invalid-title";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, rootPath, "{}"));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var updatedCourse = await service.UpdateCourseDetailsAsync(courseId, "   ", "Descrição nova");

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses.SingleAsync(course => course.Id == courseId);

        Assert.Null(updatedCourse);
        Assert.Equal($"Course {courseId:N}", persistedCourse.Title);
        Assert.Equal("Curso de teste", persistedCourse.Description);
    }

    [Fact]
    public async Task UpdateCourseDetailsAsync_DoesNotChangeProgressStatusSourceOrLocalPath()
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
        var rootPath = @"C:\courses\details-safe";
        var lessonPath = $@"{rootPath}\lesson.mp4";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "details-safe",
                RawDescription = "descrição importada",
                Title = "Details Safe",
                Description = "Descrição original",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                LifecycleStatus = CourseLifecycleStatus.Paused,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 12,
                AddedAt = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc),
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        RawTitle = "modulo",
                        RawDescription = string.Empty,
                        Title = "Modulo",
                        Description = string.Empty,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                RawTitle = "topico",
                                RawDescription = string.Empty,
                                Title = "Topico",
                                Description = string.Empty,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonId,
                                        TopicId = topicId,
                                        Order = 1,
                                        RawTitle = "lesson",
                                        RawDescription = string.Empty,
                                        Title = "Lesson",
                                        Description = string.Empty,
                                        FilePath = lessonPath,
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = lessonPath,
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 12,
                                        Status = LessonStatus.InProgress,
                                        WatchedPercentage = 65,
                                        LastPlaybackPositionSeconds = 468
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        await service.UpdateCourseDetailsAsync(courseId, "Curso editado", "Descrição editada");

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses.SingleAsync(course => course.Id == courseId);
        var lesson = await assertContext.Lessons.SingleAsync(item => item.Id == lessonId);

        Assert.Equal("details-safe", persistedCourse.RawTitle);
        Assert.Equal("descrição importada", persistedCourse.RawDescription);
        Assert.Equal("Curso editado", persistedCourse.Title);
        Assert.Equal("Descrição editada", persistedCourse.Description);
        Assert.Equal(CourseLifecycleStatus.Paused, persistedCourse.LifecycleStatus);
        Assert.Equal(CourseSourceType.LocalFolder, persistedCourse.SourceType);
        Assert.Equal(rootPath, persistedCourse.FolderPath);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(65d, lesson.WatchedPercentage);
        Assert.Equal(468, lesson.LastPlaybackPositionSeconds);
        Assert.Equal(lessonPath, lesson.LocalFilePath);
    }

    [Theory]
    [InlineData("../unsafe-module", "../unsafe-topic")]
    [InlineData("stale-module", "stale-topic")]
    public async Task GetCourseByIdAsync_RehydratesLocalStructureWithoutOverwritingEditedCourseDetails(
        string manifestModulePath,
        string manifestTopicPath)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lesson1Id = Guid.NewGuid();
        var lesson2Id = Guid.NewGuid();
        var rootPath = @"C:\courses\course-a";
        var completedAtUtc = new DateTime(2026, 3, 10, 14, 30, 0, DateTimeKind.Utc);
        var importedAtUtc = new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc);
        var lastScannedAtUtc = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc);
        const string sourceMetadataJson =
            "{\"importedAt\":\"2026-02-01T09:00:00Z\",\"lastScannedAtUtc\":\"2026-02-02T10:00:00Z\",\"customSentinel\":\"keep\"}";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "course-a",
                RawDescription = "descrição importada",
                Title = "Course A",
                Description = "Descrição original",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = sourceMetadataJson,
                TotalDurationMinutes = 20,
                AddedAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        SourceRelativePath = "preserved-module",
                        RawTitle = "modulo-1",
                        RawDescription = string.Empty,
                        Title = "Modulo 1",
                        Description = string.Empty,
                        IsAvailable = false,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                SourceRelativePath = "preserved-module/preserved-topic",
                                RawTitle = "topico-1",
                                RawDescription = string.Empty,
                                Title = "Topico 1",
                                Description = string.Empty,
                                IsAvailable = false,
                                CompletedAtUtc = completedAtUtc,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lesson1Id,
                                        TopicId = topicId,
                                        Order = 1,
                                        RawTitle = "lesson-1",
                                        RawDescription = string.Empty,
                                        Title = "Lesson 1",
                                        Description = string.Empty,
                                        FilePath = $@"{rootPath}\module-1\lesson-1.mp4",
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = $@"{rootPath}\module-1\lesson-1.mp4",
                                        IsAvailable = false,
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 20,
                                        Status = LessonStatus.Completed,
                                        WatchedPercentage = 100,
                                        LastPlaybackPositionSeconds = 1200
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        await service.UpdateCourseDetailsAsync(courseId, "Meu curso editado", string.Empty);

        await using (var setupContext = new StudyHubDbContext(options))
        {
            var manifest = new DetectedCourseStructure
            {
                CourseId = courseId,
                RootFolderName = "renamed-course",
                RootFolderPath = rootPath,
                PresentationRootRelativePath = ".",
                ScannedAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                RootNode = new DetectedFolderNode
                {
                    Name = "renamed-course",
                    RelativePath = "."
                },
                Modules =
                [
                    new DetectedModuleStructure
                    {
                        ModuleId = moduleId,
                        Order = 1,
                        RawName = "modulo-1",
                        RelativePath = manifestModulePath,
                        Topics =
                        [
                            new DetectedTopicStructure
                            {
                                TopicId = topicId,
                                Order = 1,
                                RawName = "topico-1",
                                RelativePath = manifestTopicPath,
                                Lessons =
                                [
                                    new DetectedLessonFile
                                    {
                                        LessonId = lesson1Id,
                                        Order = 1,
                                        RawName = "lesson-1",
                                        FileName = "lesson-1.mp4",
                                        RelativePath = "module-1/lesson-1.mp4",
                                        AbsolutePath = $@"{rootPath}\module-1\lesson-1.mp4",
                                        Extension = ".mp4",
                                        Duration = TimeSpan.FromMinutes(20)
                                    },
                                    new DetectedLessonFile
                                    {
                                        LessonId = lesson2Id,
                                        Order = 2,
                                        RawName = "lesson-2",
                                        FileName = "lesson-2.mp4",
                                        RelativePath = "module-1/lesson-2.mp4",
                                        AbsolutePath = $@"{rootPath}\module-1\lesson-2.mp4",
                                        Extension = ".mp4",
                                        Duration = TimeSpan.FromMinutes(15)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            var snapshot = await setupContext.CourseImportSnapshots.SingleAsync(item => item.CourseId == courseId);
            snapshot.RootFolderPath = rootPath;
            snapshot.StructureJson = JsonSerializer.Serialize(manifest, WebJsonOptions);
            snapshot.ImportedAt = DateTime.UtcNow;

            await setupContext.SaveChangesAsync();
        }

        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        Assert.Equal("Meu curso editado", loadedCourse!.Title);
        Assert.Equal(string.Empty, loadedCourse.Description);
        Assert.Equal(2, loadedCourse.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count());
        var loadedModule = Assert.Single(loadedCourse.Modules);
        var loadedTopic = Assert.Single(loadedModule.Topics);
        Assert.Equal("preserved-module", loadedModule.SourceRelativePath);
        Assert.Equal("preserved-module/preserved-topic", loadedTopic.SourceRelativePath);
        Assert.False(loadedModule.IsAvailable);
        Assert.False(loadedTopic.IsAvailable);
        Assert.Equal(completedAtUtc, loadedTopic.CompletedAtUtc);
        Assert.False(loadedTopic.Lessons.Single(lesson => lesson.Id == lesson1Id).IsAvailable);
        Assert.True(loadedTopic.Lessons.Single(lesson => lesson.Id == lesson2Id).IsAvailable);
        Assert.Equal(importedAtUtc, loadedCourse.SourceMetadata.ImportedAt);
        Assert.Equal(lastScannedAtUtc, loadedCourse.SourceMetadata.LastScannedAtUtc);

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync(course => course.Id == courseId);

        Assert.Equal("renamed-course", persistedCourse.RawTitle);
        Assert.Equal("Meu curso editado", persistedCourse.Title);
        Assert.Equal(string.Empty, persistedCourse.Description);
        Assert.Equal(2, persistedCourse.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Count());
        var persistedModule = Assert.Single(persistedCourse.Modules);
        var persistedTopic = Assert.Single(persistedModule.Topics);
        Assert.Equal("preserved-module", persistedModule.SourceRelativePath);
        Assert.Equal("preserved-module/preserved-topic", persistedTopic.SourceRelativePath);
        Assert.False(persistedModule.IsAvailable);
        Assert.False(persistedTopic.IsAvailable);
        Assert.Equal(completedAtUtc, persistedTopic.CompletedAtUtc);
        Assert.False(persistedTopic.Lessons.Single(lesson => lesson.Id == lesson1Id).IsAvailable);
        Assert.True(persistedTopic.Lessons.Single(lesson => lesson.Id == lesson2Id).IsAvailable);
        Assert.Equal(sourceMetadataJson, persistedCourse.SourceMetadataJson);

        var secondLoad = await service.GetCourseByIdAsync(courseId);
        Assert.NotNull(secondLoad);
        var secondModule = Assert.Single(secondLoad!.Modules);
        var secondTopic = Assert.Single(secondModule.Topics);
        Assert.Equal(moduleId, secondModule.Id);
        Assert.Equal(topicId, secondTopic.Id);
        Assert.Equal([lesson1Id, lesson2Id], secondTopic.Lessons.OrderBy(lesson => lesson.Order).Select(lesson => lesson.Id));
        Assert.False(secondModule.IsAvailable);
        Assert.False(secondTopic.IsAvailable);
        Assert.Equal(completedAtUtc, secondTopic.CompletedAtUtc);
        Assert.False(secondTopic.Lessons.Single(lesson => lesson.Id == lesson1Id).IsAvailable);
        Assert.True(secondTopic.Lessons.Single(lesson => lesson.Id == lesson2Id).IsAvailable);
        Assert.Equal(importedAtUtc, secondLoad.SourceMetadata.ImportedAt);
        Assert.Equal(lastScannedAtUtc, secondLoad.SourceMetadata.LastScannedAtUtc);
    }

    [Fact]
    public async Task GetCourseByIdAsync_PreservesLocalStructureAcrossCourseSwitching()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseAId = Guid.NewGuid();
        var courseBId = Guid.NewGuid();
        var moduleAId = Guid.NewGuid();
        var topicAId = Guid.NewGuid();
        var lessonA1Id = Guid.NewGuid();
        var lessonA2Id = Guid.NewGuid();
        var moduleBId = Guid.NewGuid();
        var topicBId = Guid.NewGuid();
        var lessonBId = Guid.NewGuid();

        var rootA = @"C:\courses\course-a";
        var rootB = @"C:\courses\course-b";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseAId,
                RawTitle = "course-a",
                RawDescription = "curso local a",
                Title = "Course A",
                Description = "Curso A",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootA,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 20,
                AddedAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                CurrentLessonId = lessonA1Id,
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleAId,
                        CourseId = courseAId,
                        Order = 1,
                        RawTitle = "modulo-1",
                        RawDescription = string.Empty,
                        Title = "Modulo 1",
                        Description = string.Empty,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicAId,
                                ModuleId = moduleAId,
                                Order = 1,
                                RawTitle = "topico-1",
                                RawDescription = string.Empty,
                                Title = "Topico 1",
                                Description = string.Empty,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonA1Id,
                                        TopicId = topicAId,
                                        Order = 1,
                                        RawTitle = "lesson-a1",
                                        RawDescription = string.Empty,
                                        Title = "Lesson A1",
                                        Description = string.Empty,
                                        FilePath = $@"{rootA}\module-1\lesson-a1.mp4",
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = $@"{rootA}\module-1\lesson-a1.mp4",
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 20,
                                        Status = LessonStatus.Completed,
                                        WatchedPercentage = 100,
                                        LastPlaybackPositionSeconds = 1200
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseBId,
                RawTitle = "course-b",
                RawDescription = "curso local b",
                Title = "Course B",
                Description = "Curso B",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootB,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 8,
                AddedAt = new DateTime(2026, 4, 16, 10, 5, 0, DateTimeKind.Utc),
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleBId,
                        CourseId = courseBId,
                        Order = 1,
                        RawTitle = "modulo-b",
                        RawDescription = string.Empty,
                        Title = "Modulo B",
                        Description = string.Empty,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicBId,
                                ModuleId = moduleBId,
                                Order = 1,
                                RawTitle = "topico-b",
                                RawDescription = string.Empty,
                                Title = "Topico B",
                                Description = string.Empty,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonBId,
                                        TopicId = topicBId,
                                        Order = 1,
                                        RawTitle = "lesson-b1",
                                        RawDescription = string.Empty,
                                        Title = "Lesson B1",
                                        Description = string.Empty,
                                        FilePath = $@"{rootB}\lesson-b1.mp4",
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = $@"{rootB}\lesson-b1.mp4",
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 8
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            var manifestA = new DetectedCourseStructure
            {
                CourseId = courseAId,
                RootFolderName = "course-a",
                RootFolderPath = rootA,
                PresentationRootRelativePath = ".",
                ScannedAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                RootNode = new DetectedFolderNode
                {
                    Name = "course-a",
                    RelativePath = "."
                },
                Modules =
                [
                    new DetectedModuleStructure
                    {
                        ModuleId = moduleAId,
                        Order = 1,
                        RawName = "modulo-1",
                        RelativePath = ".",
                        Topics =
                        [
                            new DetectedTopicStructure
                            {
                                TopicId = topicAId,
                                Order = 1,
                                RawName = "topico-1",
                                RelativePath = ".",
                                Lessons =
                                [
                                    new DetectedLessonFile
                                    {
                                        LessonId = lessonA1Id,
                                        Order = 1,
                                        RawName = "lesson-a1",
                                        FileName = "lesson-a1.mp4",
                                        RelativePath = "module-1/lesson-a1.mp4",
                                        AbsolutePath = $@"{rootA}\module-1\lesson-a1.mp4",
                                        Extension = ".mp4",
                                        Duration = TimeSpan.FromMinutes(20)
                                    },
                                    new DetectedLessonFile
                                    {
                                        LessonId = lessonA2Id,
                                        Order = 2,
                                        RawName = "lesson-a2",
                                        FileName = "lesson-a2.mp4",
                                        RelativePath = "module-1/lesson-a2.mp4",
                                        AbsolutePath = $@"{rootA}\module-1\lesson-a2.mp4",
                                        Extension = ".mp4",
                                        Duration = TimeSpan.FromMinutes(15)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseAId,
                SourceKind = "local-folder",
                RootFolderPath = rootA,
                StructureJson = JsonSerializer.Serialize(manifestA, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                ImportedAt = DateTime.UtcNow
            });

            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));

        var firstLoadA = await service.GetCourseByIdAsync(courseAId);
        var loadB = await service.GetCourseByIdAsync(courseBId);
        var secondLoadA = await service.GetCourseByIdAsync(courseAId);

        Assert.NotNull(firstLoadA);
        Assert.NotNull(loadB);
        Assert.NotNull(secondLoadA);

        var firstLoadALessons = firstLoadA!.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToList();
        var secondLoadALessons = secondLoadA!.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToList();

        Assert.Equal(2, firstLoadALessons.Count);
        Assert.Equal(2, secondLoadALessons.Count);
        Assert.Contains(secondLoadALessons, lesson => lesson.Id == lessonA2Id);

        var preservedLesson = secondLoadALessons.Single(lesson => lesson.Id == lessonA1Id);
        Assert.Equal(LessonStatus.Completed, preservedLesson.Status);
        Assert.Equal(100d, preservedLesson.WatchedPercentage);
    }

    [Fact]
    public async Task GetCourseByIdAsync_RehydratesLocalStructureFromManifest_WhenPersistedStructureIsPartial()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lesson1Id = Guid.NewGuid();
        var lesson2Id = Guid.NewGuid();
        var rootPath = @"C:\courses\course-a";
        var outsideLessonPath = Path.GetFullPath(Path.Combine(rootPath, "..", "outside", "lesson-2.mp4"));

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "course-a",
                RawDescription = "curso local",
                Title = "Course A",
                Description = "Curso A",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 20,
                AddedAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                CurrentLessonId = lesson1Id,
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        SourceRelativePath = string.Empty,
                        RawTitle = "modulo-1",
                        RawDescription = string.Empty,
                        Title = "Modulo 1",
                        Description = string.Empty,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                SourceRelativePath = string.Empty,
                                RawTitle = "topico-1",
                                RawDescription = string.Empty,
                                Title = "Topico 1",
                                Description = string.Empty,
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lesson1Id,
                                        TopicId = topicId,
                                        Order = 1,
                                        RawTitle = "lesson-1",
                                        RawDescription = string.Empty,
                                        Title = "Lesson 1",
                                        Description = string.Empty,
                                        FilePath = $@"{rootPath}\module-1\lesson-1.mp4",
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = $@"{rootPath}\module-1\lesson-1.mp4",
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 20,
                                        Status = LessonStatus.Completed,
                                        WatchedPercentage = 100,
                                        LastPlaybackPositionSeconds = 1200
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            var manifest = new DetectedCourseStructure
            {
                CourseId = courseId,
                RootFolderName = "course-a",
                RootFolderPath = rootPath,
                PresentationRootRelativePath = ".",
                ScannedAt = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc),
                RootNode = new DetectedFolderNode
                {
                    Name = "course-a",
                    RelativePath = "."
                },
                Modules =
                [
                    new DetectedModuleStructure
                    {
                        ModuleId = moduleId,
                        Order = 1,
                        RawName = "modulo-1",
                        RelativePath = "module-1",
                        Topics =
                        [
                            new DetectedTopicStructure
                            {
                                TopicId = topicId,
                                Order = 1,
                                RawName = "topico-1",
                                RelativePath = ".",
                                Lessons =
                                [
                                    new DetectedLessonFile
                                    {
                                        LessonId = lesson1Id,
                                        Order = 1,
                                        RawName = "lesson-1",
                                        FileName = "lesson-1.mp4",
                                        RelativePath = "module-1/lesson-1.mp4",
                                        AbsolutePath = $@"{rootPath}\module-1\lesson-1.mp4",
                                        Extension = ".mp4",
                                        Duration = TimeSpan.FromMinutes(20)
                                    },
                                    new DetectedLessonFile
                                    {
                                        LessonId = lesson2Id,
                                        Order = 2,
                                        RawName = "lesson-2",
                                        FileName = "lesson-2.mp4",
                                        RelativePath = "../outside/lesson-2.mp4",
                                        AbsolutePath = outsideLessonPath,
                                        Extension = ".mp4",
                                        Duration = TimeSpan.FromMinutes(15)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseId,
                SourceKind = "local-folder",
                RootFolderPath = rootPath,
                StructureJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                ImportedAt = DateTime.UtcNow
            });

            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        var loadedLessons = loadedCourse!.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .OrderBy(lesson => lesson.Order)
            .ToList();

        var loadedModule = Assert.Single(loadedCourse.Modules);
        var loadedTopic = Assert.Single(loadedModule.Topics);
        Assert.Equal("module-1", loadedModule.SourceRelativePath);
        Assert.Equal("module-1", loadedTopic.SourceRelativePath);

        Assert.Equal(2, loadedLessons.Count);
        var restoredLesson = loadedLessons.Single(lesson => lesson.Id == lesson1Id);
        var addedLesson = loadedLessons.Single(lesson => lesson.Id == lesson2Id);
        Assert.Equal(LessonStatus.Completed, restoredLesson.Status);
        Assert.Equal(100d, restoredLesson.WatchedPercentage);
        Assert.Equal("module-1/lesson-1.mp4", restoredLesson.RelativeFilePath);
        Assert.Equal(string.Empty, addedLesson.RelativeFilePath);

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync(course => course.Id == courseId);

        var persistedLessons = persistedCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToList();

        var persistedModule = Assert.Single(persistedCourse.Modules);
        var persistedTopic = Assert.Single(persistedModule.Topics);
        Assert.Equal("module-1", persistedModule.SourceRelativePath);
        Assert.Equal("module-1", persistedTopic.SourceRelativePath);

        Assert.Equal(2, persistedLessons.Count);
        Assert.Equal(
            "module-1/lesson-1.mp4",
            persistedLessons.Single(lesson => lesson.Id == lesson1Id).RelativeFilePath);
        Assert.Equal(
            string.Empty,
            persistedLessons.Single(lesson => lesson.Id == lesson2Id).RelativeFilePath);
        Assert.Equal(lesson1Id, persistedCourse.CurrentLessonId);
    }

    [Fact]
    public async Task GetCourseByIdAsync_StaleSubsetManifestDoesNotDeletePersistedLessons()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lesson1Id = Guid.NewGuid();
        var lesson2Id = Guid.NewGuid();
        var rootPath = @"C:\courses\stale-subset-manifest";
        var lesson1Path = Path.Combine(rootPath, "module-1", "topic-1", "lesson-1.mp4");
        var lesson2Path = Path.Combine(rootPath, "module-1", "topic-1", "lesson-2.mp4");
        var course = CreateCourseRecord(
            courseId,
            rootPath,
            JsonSerializer.Serialize(new CourseSourceMetadata { RootPath = rootPath }, WebJsonOptions));
        course.TotalDurationMinutes = 30;
        course.CurrentLessonId = lesson2Id;
        course.Modules.Add(new ModuleRecord
        {
            Id = moduleId,
            CourseId = courseId,
            Order = 1,
            RawTitle = "module-1",
            Title = "Module 1",
            SourceRelativePath = "module-1",
            Topics =
            [
                new TopicRecord
                {
                    Id = topicId,
                    ModuleId = moduleId,
                    Order = 1,
                    RawTitle = "topic-1",
                    Title = "Topic 1",
                    SourceRelativePath = "module-1/topic-1",
                    Lessons =
                    [
                        new LessonRecord
                        {
                            Id = lesson1Id,
                            TopicId = topicId,
                            Order = 1,
                            RawTitle = "lesson-1.mp4",
                            Title = "Lesson 1",
                            FilePath = lesson1Path,
                            SourceType = LessonSourceType.LocalFile,
                            LocalFilePath = lesson1Path,
                            RelativeFilePath = "module-1/topic-1/lesson-1.mp4",
                            Provider = "LocalFileSystem",
                            DurationMinutes = 10
                        },
                        new LessonRecord
                        {
                            Id = lesson2Id,
                            TopicId = topicId,
                            Order = 2,
                            RawTitle = "lesson-2.mp4",
                            Title = "Lesson 2",
                            FilePath = lesson2Path,
                            SourceType = LessonSourceType.LocalFile,
                            LocalFilePath = lesson2Path,
                            RelativeFilePath = "module-1/topic-1/lesson-2.mp4",
                            Provider = "LocalFileSystem",
                            DurationMinutes = 20,
                            Status = LessonStatus.InProgress,
                            WatchedPercentage = 62.5,
                            LastPlaybackPositionSeconds = 345,
                            IsAvailable = false
                        }
                    ]
                }
            ]
        });

        var manifest = LocalCourseManifestBuilder.Build(
            course,
            rootPath,
            new DateTime(2026, 7, 14, 19, 0, 0, DateTimeKind.Utc));
        manifest.Modules.Single().Topics.Single().Lessons.RemoveAll(
            lesson => lesson.LessonId == lesson2Id);

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(course);
            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseId,
                SourceKind = "local-folder",
                RootFolderPath = rootPath,
                StructureJson = JsonSerializer.Serialize(manifest, WebJsonOptions),
                ImportedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)
            });
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var firstLoad = await service.GetCourseByIdAsync(courseId);
        var secondLoad = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(firstLoad);
        Assert.NotNull(secondLoad);
        foreach (var loadedCourse in new[] { firstLoad!, secondLoad! })
        {
            var lessons = loadedCourse.Modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .OrderBy(lesson => lesson.Order)
                .ToList();
            Assert.Equal([lesson1Id, lesson2Id], lessons.Select(lesson => lesson.Id));
            var preservedLesson = lessons.Single(lesson => lesson.Id == lesson2Id);
            Assert.Equal(LessonStatus.InProgress, preservedLesson.Status);
            Assert.Equal(62.5, preservedLesson.WatchedPercentage);
            Assert.Equal(TimeSpan.FromSeconds(345), preservedLesson.LastPlaybackPosition);
            Assert.False(preservedLesson.IsAvailable);
        }

        await using var subsetAssertContext = new StudyHubDbContext(options);
        Assert.Equal(
            lesson2Id,
            (await subsetAssertContext.Courses.SingleAsync(item => item.Id == courseId)).CurrentLessonId);
    }

    [Fact]
    public async Task GetCourseByIdAsync_StaleSnapshotRootDoesNotRestorePreviousLocation()
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
        var currentRootPath = @"C:\courses\current-location";
        var staleRootPath = @"C:\courses\previous-location";
        const string relativePath = "module-1/topic-1/lesson-1.mp4";
        var currentLessonPath = Path.Combine(
            currentRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var course = CreateCourseRecord(
            courseId,
            currentRootPath,
            JsonSerializer.Serialize(
                new CourseSourceMetadata { RootPath = currentRootPath },
                WebJsonOptions));
        course.CurrentLessonId = lessonId;
        course.Modules.Add(new ModuleRecord
        {
            Id = moduleId,
            CourseId = courseId,
            Order = 1,
            RawTitle = "module-1",
            Title = "Module 1",
            SourceRelativePath = "module-1",
            Topics =
            [
                new TopicRecord
                {
                    Id = topicId,
                    ModuleId = moduleId,
                    Order = 1,
                    RawTitle = "topic-1",
                    Title = "Topic 1",
                    SourceRelativePath = "module-1/topic-1",
                    Lessons =
                    [
                        new LessonRecord
                        {
                            Id = lessonId,
                            TopicId = topicId,
                            Order = 1,
                            RawTitle = "lesson-1.mp4",
                            Title = "Lesson 1",
                            FilePath = currentLessonPath,
                            SourceType = LessonSourceType.LocalFile,
                            LocalFilePath = currentLessonPath,
                            RelativeFilePath = relativePath,
                            Provider = "LocalFileSystem",
                            DurationMinutes = 10,
                            Status = LessonStatus.InProgress,
                            WatchedPercentage = 35,
                            LastPlaybackPositionSeconds = 90
                        }
                    ]
                }
            ]
        });
        var staleManifest = LocalCourseManifestBuilder.Build(
            course,
            staleRootPath,
            new DateTime(2026, 7, 14, 19, 15, 0, DateTimeKind.Utc));

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(course);
            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseId,
                SourceKind = "local-folder",
                RootFolderPath = staleRootPath,
                StructureJson = JsonSerializer.Serialize(staleManifest, WebJsonOptions),
                ImportedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)
            });
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var firstLoad = await service.GetCourseByIdAsync(courseId);
        var secondLoad = await service.GetCourseByIdAsync(courseId);

        foreach (var loadedCourse in new[] { Assert.IsType<Course>(firstLoad), Assert.IsType<Course>(secondLoad) })
        {
            Assert.Equal(currentRootPath, loadedCourse.FolderPath);
            Assert.Equal(currentRootPath, loadedCourse.SourceMetadata.RootPath);
            var lesson = Assert.Single(
                loadedCourse.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
            Assert.Equal(lessonId, lesson.Id);
            Assert.Equal(currentLessonPath, lesson.LocalFilePath);
            Assert.Equal(relativePath, lesson.RelativeFilePath);
            Assert.Equal(LessonStatus.InProgress, lesson.Status);
        }


        await using var rootAssertContext = new StudyHubDbContext(options);
        Assert.Equal(
            lessonId,
            (await rootAssertContext.Courses.SingleAsync(item => item.Id == courseId)).CurrentLessonId);
    }

    [Fact]
    public async Task GetCourseByIdAsync_RehydrationFailureRollsBackExistingTree()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var existingLessonId = Guid.NewGuid();
        var newLessonId = Guid.NewGuid();
        var rootPath = @"C:\courses\rehydration-rollback";
        const string existingRelativePath = "module-1/topic-1/lesson-1.mp4";
        const string failingRelativePath = "module-1/topic-1/forced-failure.mp4";
        var existingLessonPath = Path.Combine(
            rootPath,
            existingRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var course = CreateCourseRecord(
            courseId,
            rootPath,
            JsonSerializer.Serialize(new CourseSourceMetadata { RootPath = rootPath }, WebJsonOptions));
        course.CurrentLessonId = existingLessonId;
        course.Modules.Add(new ModuleRecord
        {
            Id = moduleId,
            CourseId = courseId,
            Order = 1,
            RawTitle = "module-1",
            Title = "Module 1",
            SourceRelativePath = "module-1",
            Topics =
            [
                new TopicRecord
                {
                    Id = topicId,
                    ModuleId = moduleId,
                    Order = 1,
                    RawTitle = "topic-1",
                    Title = "Topic 1",
                    SourceRelativePath = "module-1/topic-1",
                    Lessons =
                    [
                        new LessonRecord
                        {
                            Id = existingLessonId,
                            TopicId = topicId,
                            Order = 1,
                            RawTitle = "lesson-1.mp4",
                            Title = "Lesson 1",
                            FilePath = existingLessonPath,
                            SourceType = LessonSourceType.LocalFile,
                            LocalFilePath = existingLessonPath,
                            RelativeFilePath = existingRelativePath,
                            Provider = "LocalFileSystem",
                            DurationMinutes = 10,
                            Status = LessonStatus.Completed,
                            WatchedPercentage = 100,
                            LastPlaybackPositionSeconds = 600
                        }
                    ]
                }
            ]
        });
        var manifest = LocalCourseManifestBuilder.Build(
            course,
            rootPath,
            new DateTime(2026, 7, 14, 19, 30, 0, DateTimeKind.Utc));
        manifest.Modules.Single().Topics.Single().Lessons.Add(new DetectedLessonFile
        {
            LessonId = newLessonId,
            Order = 2,
            RawName = "forced-failure.mp4",
            FileName = "forced-failure.mp4",
            RelativePath = failingRelativePath,
            AbsolutePath = Path.Combine(
                rootPath,
                failingRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Extension = ".mp4",
            Duration = TimeSpan.FromMinutes(5)
        });

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(course);
            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseId,
                SourceKind = "local-folder",
                RootFolderPath = rootPath,
                StructureJson = JsonSerializer.Serialize(manifest, WebJsonOptions),
                ImportedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)
            });
            await setupContext.SaveChangesAsync();
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_rehydration_lesson_insert
                BEFORE INSERT ON lessons
                WHEN NEW.raw_title = 'forced-failure.mp4'
                BEGIN
                    SELECT RAISE(ABORT, 'forced rehydration failure');
                END;
                """);
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.GetCourseByIdAsync(courseId));

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses
            .AsNoTracking()
            .Include(item => item.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync(item => item.Id == courseId);
        var persistedLesson = Assert.Single(
            persistedCourse.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        Assert.Equal(existingLessonId, persistedLesson.Id);
        Assert.Equal(LessonStatus.Completed, persistedLesson.Status);
        Assert.Equal(100, persistedLesson.WatchedPercentage);
        Assert.Equal(existingLessonId, persistedCourse.CurrentLessonId);
    }

    [Fact]
    public async Task GetCourseByIdAsync_RehydratesEmptyKnownTopicAndKeepsItOnSecondLoad()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var populatedTopicId = Guid.NewGuid();
        var emptyTopicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var rootPath = @"C:\courses\empty-known-topic";
        var relativeLessonPath = "module-1/topic-1/lesson-1.mp4";
        var absoluteLessonPath = Path.Combine(rootPath, "module-1", "topic-1", "lesson-1.mp4");
        var manifest = new DetectedCourseStructure
        {
            CourseId = courseId,
            RootFolderName = "empty-known-topic",
            RootFolderPath = rootPath,
            PresentationRootRelativePath = ".",
            ScannedAt = new DateTime(2026, 7, 14, 18, 30, 0, DateTimeKind.Utc),
            RootNode = new DetectedFolderNode
            {
                Name = "empty-known-topic",
                RelativePath = "."
            },
            Modules =
            [
                new DetectedModuleStructure
                {
                    ModuleId = moduleId,
                    Order = 1,
                    RawName = "module-1",
                    RelativePath = "module-1",
                    Topics =
                    [
                        new DetectedTopicStructure
                        {
                            TopicId = populatedTopicId,
                            Order = 1,
                            RawName = "topic-1",
                            RelativePath = "topic-1",
                            Lessons =
                            [
                                new DetectedLessonFile
                                {
                                    LessonId = lessonId,
                                    Order = 1,
                                    RawName = "lesson-1.mp4",
                                    FileName = "lesson-1.mp4",
                                    RelativePath = relativeLessonPath,
                                    AbsolutePath = absoluteLessonPath,
                                    Extension = ".mp4",
                                    Duration = TimeSpan.FromMinutes(10)
                                }
                            ]
                        },
                        new DetectedTopicStructure
                        {
                            TopicId = emptyTopicId,
                            Order = 2,
                            RawName = "empty-topic",
                            RelativePath = "empty-topic",
                            Lessons = []
                        }
                    ]
                }
            ]
        };

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "empty-known-topic",
                Title = "Empty known topic",
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 10,
                AddedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        RawTitle = "module-1",
                        Title = "Module 1",
                        SourceRelativePath = "module-1",
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = populatedTopicId,
                                ModuleId = moduleId,
                                Order = 1,
                                RawTitle = "topic-1",
                                Title = "Topic 1",
                                SourceRelativePath = "module-1/topic-1",
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonId,
                                        TopicId = populatedTopicId,
                                        Order = 1,
                                        RawTitle = "lesson-1.mp4",
                                        Title = "Lesson 1",
                                        LocalFilePath = absoluteLessonPath,
                                        FilePath = absoluteLessonPath,
                                        RelativeFilePath = relativeLessonPath,
                                        SourceType = LessonSourceType.LocalFile,
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 10
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });
            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseId,
                SourceKind = "local-folder",
                RootFolderPath = rootPath,
                StructureJson = JsonSerializer.Serialize(manifest, WebJsonOptions),
                ImportedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)
            });
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));

        var firstLoad = await service.GetCourseByIdAsync(courseId);
        var secondLoad = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(firstLoad);
        Assert.NotNull(secondLoad);
        Assert.Equal(
            [populatedTopicId, emptyTopicId],
            firstLoad!.Modules.Single().Topics.OrderBy(topic => topic.Order).Select(topic => topic.Id));
        Assert.Empty(firstLoad.Modules.Single().Topics.Single(topic => topic.Id == emptyTopicId).Lessons);
        Assert.Equal(
            firstLoad.Modules.Single().Topics.Select(topic => topic.Id).Order(),
            secondLoad!.Modules.Single().Topics.Select(topic => topic.Id).Order());
    }

    [Fact]
    public async Task GetCourseByIdAsync_RebuildsSnapshotWhoseManifestCourseIdDoesNotMatchRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var courseId = Guid.NewGuid();
        var mismatchedManifestCourseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var rootPath = @"C:\courses\mismatched-manifest-course-id";
        var relativeLessonPath = "module-1/topic-1/lesson-1.mp4";
        var absoluteLessonPath = Path.Combine(rootPath, "module-1", "topic-1", "lesson-1.mp4");
        var originalSnapshotImportedAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "mismatched-manifest-course-id",
                Title = "Mismatched manifest course id",
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 10,
                AddedAt = originalSnapshotImportedAt,
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        RawTitle = "module-1",
                        Title = "Module 1",
                        SourceRelativePath = "module-1",
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                RawTitle = "topic-1",
                                Title = "Topic 1",
                                SourceRelativePath = "module-1/topic-1",
                                Lessons =
                                [
                                    new LessonRecord
                                    {
                                        Id = lessonId,
                                        TopicId = topicId,
                                        Order = 1,
                                        RawTitle = "lesson-1.mp4",
                                        Title = "Lesson 1",
                                        LocalFilePath = absoluteLessonPath,
                                        FilePath = absoluteLessonPath,
                                        RelativeFilePath = relativeLessonPath,
                                        SourceType = LessonSourceType.LocalFile,
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 10
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });
            setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
            {
                CourseId = courseId,
                SourceKind = "local-folder",
                RootFolderPath = rootPath,
                ImportedAt = originalSnapshotImportedAt,
                StructureJson = JsonSerializer.Serialize(new DetectedCourseStructure
                {
                    CourseId = mismatchedManifestCourseId,
                    RootFolderName = "mismatched-manifest-course-id",
                    RootFolderPath = rootPath,
                    ScannedAt = originalSnapshotImportedAt,
                    Modules =
                    [
                        new DetectedModuleStructure
                        {
                            ModuleId = moduleId,
                            Order = 1,
                            RelativePath = "module-1",
                            Topics =
                            [
                                new DetectedTopicStructure
                                {
                                    TopicId = topicId,
                                    Order = 1,
                                    RelativePath = "topic-1",
                                    Lessons =
                                    [
                                        new DetectedLessonFile
                                        {
                                            LessonId = lessonId,
                                            Order = 1,
                                            FileName = "lesson-1.mp4",
                                            RelativePath = relativeLessonPath,
                                            AbsolutePath = absoluteLessonPath
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }, WebJsonOptions)
            });
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));

        var firstLoad = await service.GetCourseByIdAsync(courseId);
        var secondLoad = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(firstLoad);
        Assert.NotNull(secondLoad);
        Assert.Equal(courseId, firstLoad!.Id);
        Assert.Equal(courseId, secondLoad!.Id);

        await using var assertContext = new StudyHubDbContext(options);
        Assert.Equal([courseId], await assertContext.Courses
            .AsNoTracking()
            .Select(course => course.Id)
            .ToListAsync());
        var repairedSnapshot = await assertContext.CourseImportSnapshots
            .AsNoTracking()
            .SingleAsync(snapshot => snapshot.CourseId == courseId);
        var repairedManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(
            repairedSnapshot.StructureJson,
            WebJsonOptions);
        Assert.NotNull(repairedManifest);
        Assert.Equal(courseId, repairedManifest!.CourseId);
        Assert.Equal(moduleId, Assert.Single(repairedManifest.Modules).ModuleId);
        Assert.Equal(lessonId, Assert.Single(repairedManifest.Modules.Single().Topics.Single().Lessons).LessonId);
        Assert.Equal(originalSnapshotImportedAt, repairedSnapshot.ImportedAt);
        Assert.False(await assertContext.Courses.AnyAsync(course => course.Id == mismatchedManifestCourseId));
    }

    [Fact]
    public async Task GetCourseByIdAsync_CreatesLocalManifestForLegacyCourse_WhenMissing()
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
        var rootPath = @"C:\courses\legacy-course";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "legacy-course",
                RawDescription = "curso legado",
                Title = "Legacy Course",
                Description = "Curso legado",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = rootPath,
                SourceType = CourseSourceType.LocalFolder,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 10,
                AddedAt = new DateTime(2026, 4, 15, 9, 0, 0, DateTimeKind.Utc),
                Modules =
                [
                    new ModuleRecord
                    {
                        Id = moduleId,
                        CourseId = courseId,
                        Order = 1,
                        SourceRelativePath = "module-1",
                        RawTitle = "modulo-1",
                        RawDescription = string.Empty,
                        Title = "Modulo 1",
                        Description = string.Empty,
                        Topics =
                        [
                            new TopicRecord
                            {
                                Id = topicId,
                                ModuleId = moduleId,
                                Order = 1,
                                SourceRelativePath = "module-1/topic-1",
                                RawTitle = "topico-1",
                                RawDescription = string.Empty,
                                Title = "Topico 1",
                                Description = string.Empty,
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
                                        FilePath = $@"{rootPath}\lesson-1.mp4",
                                        SourceType = LessonSourceType.LocalFile,
                                        LocalFilePath = $@"{rootPath}\lesson-1.mp4",
                                        Provider = "LocalFileSystem",
                                        DurationMinutes = 10
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        Assert.Equal(courseId, loadedCourse!.Id);
        Assert.Single(loadedCourse.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        var loadedModule = Assert.Single(loadedCourse.Modules);
        var loadedTopic = Assert.Single(loadedModule.Topics);
        Assert.Equal("module-1", loadedModule.SourceRelativePath);
        Assert.Equal("module-1/topic-1", loadedTopic.SourceRelativePath);

        await using var assertContext = new StudyHubDbContext(options);
        var snapshot = await assertContext.CourseImportSnapshots.SingleAsync(item => item.CourseId == courseId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StructureJson));

        var manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(
            snapshot.StructureJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(manifest);
        Assert.Equal(courseId, manifest!.CourseId);
        var manifestModule = Assert.Single(manifest.Modules);
        var manifestTopic = Assert.Single(manifestModule.Topics);
        Assert.Equal("module-1", manifestModule.RelativePath);
        Assert.Equal("topic-1", manifestTopic.RelativePath);
        var manifestLessons = manifest.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToList();

        Assert.Single(manifestLessons);
        Assert.Equal(lessonId, manifestLessons[0].LessonId);
    }

    [Fact]
    public async Task GetCourseByIdAsync_LoadsIntroSkipMetadata_FromPersistedSourceMetadataJson()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\intro-course";
        var metadata = new CourseSourceMetadata
        {
            RootPath = rootPath,
            Provider = "LocalFileSystem",
            IntroSkipEnabled = true,
            IntroSkipSeconds = 32
        };

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(
                courseId,
                rootPath,
                JsonSerializer.Serialize(metadata, WebJsonOptions)));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        Assert.True(loadedCourse!.SourceMetadata.IntroSkipEnabled);
        Assert.Equal(32, loadedCourse.SourceMetadata.IntroSkipSeconds);
    }

    [Fact]
    public async Task GetCourseByIdAsync_DefaultsIntroSkipMetadata_WhenLegacyJsonDoesNotContainFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\legacy-intro-skip";
        var legacyMetadataJson = JsonSerializer.Serialize(new
        {
            rootPath,
            provider = "LocalFileSystem",
            scanVersion = "legacy-local-v1"
        }, WebJsonOptions);

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, rootPath, legacyMetadataJson));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        Assert.False(loadedCourse!.SourceMetadata.IntroSkipEnabled);
        Assert.Equal(0, loadedCourse.SourceMetadata.IntroSkipSeconds);
    }

    [Fact]
    public async Task GetCourseByIdAsync_NormalizesNegativeIntroSkipSeconds()
    {
        var metadata = new CourseSourceMetadata
        {
            RootPath = @"C:\courses\normalization-check",
            Provider = "LocalFileSystem",
            IntroSkipEnabled = true,
            IntroSkipSeconds = -25
        };

        Assert.Equal(0, metadata.IntroSkipSeconds);

        var normalizedJson = JsonSerializer.Serialize(metadata, WebJsonOptions);
        using (var normalizedJsonDoc = JsonDocument.Parse(normalizedJson))
        {
            Assert.Equal(0, normalizedJsonDoc.RootElement.GetProperty("introSkipSeconds").GetInt32());
        }

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\negative-intro-skip";
        var negativeMetadataJson = JsonSerializer.Serialize(new
        {
            rootPath,
            provider = "LocalFileSystem",
            introSkipEnabled = true,
            introSkipSeconds = -40
        }, WebJsonOptions);

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, rootPath, negativeMetadataJson));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));
        var loadedCourse = await service.GetCourseByIdAsync(courseId);

        Assert.NotNull(loadedCourse);
        Assert.True(loadedCourse!.SourceMetadata.IntroSkipEnabled);
        Assert.Equal(0, loadedCourse.SourceMetadata.IntroSkipSeconds);
    }

    [Fact]
    public async Task UpdateCourseIntroSkipPreferenceAsync_PersistsUpdatedValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\update-intro-skip";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, rootPath, "{}"));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));

        var updatedMetadata = await service.UpdateCourseIntroSkipPreferenceAsync(
            courseId,
            introSkipEnabled: true,
            introSkipSeconds: 27);

        Assert.NotNull(updatedMetadata);
        Assert.True(updatedMetadata!.IntroSkipEnabled);
        Assert.Equal(27, updatedMetadata.IntroSkipSeconds);

        var loadedCourse = await service.GetCourseByIdAsync(courseId);
        Assert.NotNull(loadedCourse);
        Assert.True(loadedCourse!.SourceMetadata.IntroSkipEnabled);
        Assert.Equal(27, loadedCourse.SourceMetadata.IntroSkipSeconds);
    }

    [Fact]
    public async Task UpdateCourseIntroSkipPreferenceAsync_NormalizesNegativeSeconds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var courseId = Guid.NewGuid();
        var rootPath = @"C:\courses\normalize-update-intro-skip";

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(CreateCourseRecord(courseId, rootPath, "{}"));
            await setupContext.SaveChangesAsync();
        }

        var service = new PersistedCourseService(new TestDbContextFactory(options));

        var updatedMetadata = await service.UpdateCourseIntroSkipPreferenceAsync(
            courseId,
            introSkipEnabled: true,
            introSkipSeconds: -9);

        Assert.NotNull(updatedMetadata);
        Assert.True(updatedMetadata!.IntroSkipEnabled);
        Assert.Equal(0, updatedMetadata.IntroSkipSeconds);

        var loadedCourse = await service.GetCourseByIdAsync(courseId);
        Assert.NotNull(loadedCourse);
        Assert.True(loadedCourse!.SourceMetadata.IntroSkipEnabled);
        Assert.Equal(0, loadedCourse.SourceMetadata.IntroSkipSeconds);
    }

    private static CourseRecord CreateCourseRecord(Guid id, string rootPath, string sourceMetadataJson)
    {
        return new CourseRecord
        {
            Id = id,
            RawTitle = $"course-{id:N}",
            RawDescription = "curso para teste de metadata",
            Title = $"Course {id:N}",
            Description = "Curso de teste",
            Category = "Curso Local",
            ThumbnailUrl = string.Empty,
            FolderPath = rootPath,
            SourceType = CourseSourceType.LocalFolder,
            SourceMetadataJson = sourceMetadataJson,
            TotalDurationMinutes = 0,
            AddedAt = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc),
            Modules = []
        };
    }

    private sealed class TestStoragePathsService : IStoragePathsService
    {
        public TestStoragePathsService(string rootDirectory)
        {
            AppDataDirectory = rootDirectory;
            DatabaseDirectory = rootDirectory;
            BackupsDirectory = Path.Combine(rootDirectory, "backups");
            DatabasePath = Path.Combine(rootDirectory, "studyhub.db");
            RoutineDirectory = Path.Combine(rootDirectory, "routine");
            EnsureStorageDirectories();
        }

        public string AppDataDirectory { get; }
        public string DatabaseDirectory { get; }
        public string DatabasePath { get; }
        public string BackupsDirectory { get; }
        public string RoutineDirectory { get; }

        public void EnsureStorageDirectories()
        {
            Directory.CreateDirectory(AppDataDirectory);
            Directory.CreateDirectory(DatabaseDirectory);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(RoutineDirectory);
        }

        public bool IsManagedPath(string path) => true;
        public bool IsBackupPath(string path) => false;
        public string CreateUniqueBackupDirectory(string prefix) => Path.Combine(BackupsDirectory, prefix);
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudyHubDbContext> options) : IDbContextFactory<StudyHubDbContext>
    {
        private readonly DbContextOptions<StudyHubDbContext> _options = options;

        public StudyHubDbContext CreateDbContext()
            => new(_options);

        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new StudyHubDbContext(_options));
    }
}
