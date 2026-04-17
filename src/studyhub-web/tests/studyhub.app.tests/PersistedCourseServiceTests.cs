using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
                                        ExternalUrl = string.Empty,
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
                                        ExternalUrl = string.Empty,
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
                                        ExternalUrl = string.Empty,
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
                        RelativePath = ".",
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

        Assert.Equal(2, loadedLessons.Count);
        var restoredLesson = loadedLessons.Single(lesson => lesson.Id == lesson1Id);
        Assert.Equal(LessonStatus.Completed, restoredLesson.Status);
        Assert.Equal(100d, restoredLesson.WatchedPercentage);

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

        Assert.Equal(2, persistedLessons.Count);
        Assert.Equal(lesson1Id, persistedCourse.CurrentLessonId);
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
                                        ExternalUrl = string.Empty,
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

        await using var assertContext = new StudyHubDbContext(options);
        var snapshot = await assertContext.CourseImportSnapshots.SingleAsync(item => item.CourseId == courseId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StructureJson));

        var manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(
            snapshot.StructureJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(manifest);
        Assert.Equal(courseId, manifest!.CourseId);
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

    private sealed class TestDbContextFactory(DbContextOptions<StudyHubDbContext> options) : IDbContextFactory<StudyHubDbContext>
    {
        private readonly DbContextOptions<StudyHubDbContext> _options = options;

        public StudyHubDbContext CreateDbContext()
            => new(_options);

        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new StudyHubDbContext(_options));
    }
}
