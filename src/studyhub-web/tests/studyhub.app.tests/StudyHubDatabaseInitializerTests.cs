using System.Data;
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

public sealed class StudyHubDatabaseInitializerTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task InitializeAsync_NewDatabaseCreatesTopicCompletionColumnWithNullDefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            Assert.True(await ColumnExistsAsync(options, "topics", "completed_at_utc"));
            Assert.Equal(13, await GetSchemaVersionAsync(options));

            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();

            await using (var setupContext = new StudyHubDbContext(options))
            {
                setupContext.Courses.Add(CreateCourseRecord(courseId, moduleId, topicId, lessonId));
                await setupContext.SaveChangesAsync();
            }

            await using var assertContext = new StudyHubDbContext(options);
            var topic = await assertContext.Topics.SingleAsync(item => item.Id == topicId);

            Assert.Null(topic.CompletedAtUtc);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task TopicCompletionTimestamp_PersistsAcrossDbContextAndDomainReads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var completedAtUtc = new DateTime(2026, 7, 6, 12, 30, 0, DateTimeKind.Utc);

            await using (var setupContext = new StudyHubDbContext(options))
            {
                setupContext.Courses.Add(CreateCourseRecord(courseId, moduleId, topicId, lessonId, completedAtUtc));
                await setupContext.SaveChangesAsync();
            }

            await using (var assertContext = new StudyHubDbContext(options))
            {
                var topic = await assertContext.Topics.SingleAsync(item => item.Id == topicId);
                Assert.Equal(completedAtUtc, topic.CompletedAtUtc);
            }

            var service = new PersistedCourseService(new TestDbContextFactory(options));
            var loadedCourse = await service.GetCourseByIdAsync(courseId);

            Assert.NotNull(loadedCourse);
            var module = Assert.Single(loadedCourse!.Modules);
            var topicFromDomain = Assert.Single(module.Topics);
            var lesson = Assert.Single(topicFromDomain.Lessons);

            Assert.Equal(moduleId, module.Id);
            Assert.Equal(topicId, topicFromDomain.Id);
            Assert.Equal(lessonId, lesson.Id);
            Assert.Equal(completedAtUtc, topicFromDomain.CompletedAtUtc);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_NewDatabaseCreatesAvailabilityColumnsAndPersistsExplicitFalse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            await CreateInitializer(options, storageRoot).InitializeAsync();

            await AssertAvailabilityColumnAsync(options, "modules");
            await AssertAvailabilityColumnAsync(options, "topics");
            await AssertAvailabilityColumnAsync(options, "lessons");
            Assert.Equal(13, await GetSchemaVersionAsync(options));

            Assert.True(new Module().IsAvailable);
            Assert.True(new Topic().IsAvailable);
            Assert.True(new Lesson().IsAvailable);
            Assert.True(new ModuleRecord().IsAvailable);
            Assert.True(new TopicRecord().IsAvailable);
            Assert.True(new LessonRecord().IsAvailable);

            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var lastScannedAtUtc = new DateTime(2026, 7, 14, 17, 0, 0, DateTimeKind.Utc);
            var course = CreateCourseRecord(courseId, moduleId, topicId, lessonId);
            course.SourceMetadataJson = JsonSerializer.Serialize(new CourseSourceMetadata
            {
                RootPath = course.FolderPath,
                ImportedAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
                LastScannedAtUtc = lastScannedAtUtc,
                ScanVersion = "local-folder-v1",
                Provider = "LocalFileSystem"
            }, WebJsonOptions);
            course.Modules.Single().IsAvailable = false;
            course.Modules.Single().Topics.Single().IsAvailable = false;
            course.Modules.Single().Topics.Single().Lessons.Single().IsAvailable = false;

            await using (var setupContext = new StudyHubDbContext(options))
            {
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            await using var assertContext = new StudyHubDbContext(options);
            var persisted = await assertContext.Courses
                .AsNoTracking()
                .Include(item => item.Modules)
                    .ThenInclude(item => item.Topics)
                        .ThenInclude(item => item.Lessons)
                .SingleAsync(item => item.Id == courseId);
            var persistedModule = Assert.Single(persisted.Modules);
            var persistedTopic = Assert.Single(persistedModule.Topics);
            var persistedLesson = Assert.Single(persistedTopic.Lessons);

            Assert.False(persistedModule.IsAvailable);
            Assert.False(persistedTopic.IsAvailable);
            Assert.False(persistedLesson.IsAvailable);

            var domain = persisted.ToDomain();
            Assert.False(Assert.Single(domain.Modules).IsAvailable);
            Assert.False(Assert.Single(domain.Modules.Single().Topics).IsAvailable);
            Assert.False(Assert.Single(domain.Modules.Single().Topics.Single().Lessons).IsAvailable);
            Assert.Equal(lastScannedAtUtc, domain.SourceMetadata.LastScannedAtUtc);

            var roundTrippedRecord = domain.ToRecord();
            Assert.False(Assert.Single(roundTrippedRecord.Modules).IsAvailable);
            Assert.False(Assert.Single(roundTrippedRecord.Modules.Single().Topics).IsAvailable);
            Assert.False(Assert.Single(roundTrippedRecord.Modules.Single().Topics.Single().Lessons).IsAvailable);
            var roundTrippedMetadata = JsonSerializer.Deserialize<CourseSourceMetadata>(
                roundTrippedRecord.SourceMetadataJson,
                WebJsonOptions);
            Assert.Equal(lastScannedAtUtc, roundTrippedMetadata?.LastScannedAtUtc);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_UpgradesSchema12AvailabilityAndPreservesExistingStateIdempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var completedAtUtc = new DateTime(2026, 7, 10, 14, 30, 0, DateTimeKind.Utc);
            const string moduleRelativePath = "Modulo 01";
            const string topicRelativePath = "Modulo 01/Topico 01";
            const string lessonRelativePath = "Modulo 01/Topico 01/Aula 01.mp4";
            var course = CreateCourseRecord(courseId, moduleId, topicId, lessonId, completedAtUtc);
            course.CurrentLessonId = lessonId;
            var module = course.Modules.Single();
            module.SourceRelativePath = moduleRelativePath;
            var topic = module.Topics.Single();
            topic.SourceRelativePath = topicRelativePath;
            var lesson = topic.Lessons.Single();
            lesson.RelativeFilePath = lessonRelativePath;
            lesson.Status = LessonStatus.InProgress;
            lesson.WatchedPercentage = 46.5;
            lesson.LastPlaybackPositionSeconds = 91;

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
                await setupContext.Database.ExecuteSqlRawAsync("PRAGMA user_version = 12;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE lessons DROP COLUMN is_available;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE topics DROP COLUMN is_available;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE modules DROP COLUMN is_available;");
            }

            Assert.False(await ColumnExistsAsync(options, "modules", "is_available"));
            Assert.False(await ColumnExistsAsync(options, "topics", "is_available"));
            Assert.False(await ColumnExistsAsync(options, "lessons", "is_available"));

            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            await AssertAvailabilityColumnAsync(options, "modules");
            await AssertAvailabilityColumnAsync(options, "topics");
            await AssertAvailabilityColumnAsync(options, "lessons");
            Assert.Equal(13, await GetSchemaVersionAsync(options));

            await using (var firstAssertContext = new StudyHubDbContext(options))
            {
                var persisted = await firstAssertContext.Courses
                    .AsNoTracking()
                    .Include(item => item.Modules)
                        .ThenInclude(item => item.Topics)
                            .ThenInclude(item => item.Lessons)
                    .SingleAsync(item => item.Id == courseId);

                AssertAvailabilityUpgradeState(
                    persisted,
                    moduleId,
                    topicId,
                    lessonId,
                    completedAtUtc,
                    expectedAvailability: true);
            }

            await using (var updateContext = new StudyHubDbContext(options))
            {
                var persisted = await updateContext.Courses
                    .Include(item => item.Modules)
                        .ThenInclude(item => item.Topics)
                            .ThenInclude(item => item.Lessons)
                    .SingleAsync(item => item.Id == courseId);
                persisted.Modules.Single().IsAvailable = false;
                persisted.Modules.Single().Topics.Single().IsAvailable = false;
                persisted.Modules.Single().Topics.Single().Lessons.Single().IsAvailable = false;
                await updateContext.SaveChangesAsync();
            }

            await initializer.InitializeAsync();
            await initializer.InitializeAsync();

            await using var finalAssertContext = new StudyHubDbContext(options);
            var finalPersisted = await finalAssertContext.Courses
                .AsNoTracking()
                .Include(item => item.Modules)
                    .ThenInclude(item => item.Topics)
                        .ThenInclude(item => item.Lessons)
                .SingleAsync(item => item.Id == courseId);

            AssertAvailabilityUpgradeState(
                finalPersisted,
                moduleId,
                topicId,
                lessonId,
                completedAtUtc,
                expectedAvailability: false);
            Assert.Equal(13, await GetSchemaVersionAsync(options));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_UpgradesLegacyDatabaseAddingTopicCompletionColumn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(CreateCourseRecord(courseId, moduleId, topicId, lessonId));
                await setupContext.SaveChangesAsync();
                await setupContext.Database.ExecuteSqlRawAsync("PRAGMA user_version = 9;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE topics DROP COLUMN completed_at_utc;");
            }

            Assert.False(await ColumnExistsAsync(options, "topics", "completed_at_utc"));

            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            Assert.True(await ColumnExistsAsync(options, "topics", "completed_at_utc"));
            Assert.Equal(13, await GetSchemaVersionAsync(options));

            var service = new PersistedCourseService(new TestDbContextFactory(options));
            var loadedCourse = await service.GetCourseByIdAsync(courseId);

            Assert.NotNull(loadedCourse);
            var module = Assert.Single(loadedCourse!.Modules);
            var topic = Assert.Single(module.Topics);
            var lesson = Assert.Single(topic.Lessons);

            Assert.Equal(moduleId, module.Id);
            Assert.Equal(topicId, topic.Id);
            Assert.Equal(lessonId, lesson.Id);
            Assert.Null(topic.CompletedAtUtc);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotentForTopicCompletionSchemaUpgrade()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            var initializer = CreateInitializer(options, storageRoot);

            await initializer.InitializeAsync();
            await initializer.InitializeAsync();

            Assert.True(await ColumnExistsAsync(options, "topics", "completed_at_utc"));
            Assert.Equal(13, await GetSchemaVersionAsync(options));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_NewDatabaseCreatesLessonRelativeFilePathColumnWithRequiredEmptyDefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            var column = await GetColumnInfoAsync(options, "lessons", "relative_file_path");

            Assert.NotNull(column);
            Assert.Equal("TEXT", column!.StoreType, ignoreCase: true);
            Assert.True(column.IsNotNull);
            Assert.Equal("''", column.DefaultValue);
            Assert.Equal(13, await GetSchemaVersionAsync(options));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_UpgradesSchema10AndBackfillsRelativeFilePathWithoutChangingLessonState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var courseRoot = Path.Combine(Path.GetTempPath(), "studyhub-relative-path-tests", Guid.NewGuid().ToString("N"), "Curso A");
        var lessonPath = Path.Combine(courseRoot, "Modulo 01", "Topico 01", "Aula 01.mp4");
        const double watchedPercentage = 47.5;
        const int lastPlaybackPositionSeconds = 321;

        try
        {
            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var course = CreateCourseRecord(
                courseId,
                moduleId,
                topicId,
                lessonId,
                rootPath: courseRoot,
                lessonPath: lessonPath);
            var lesson = course.Modules.Single().Topics.Single().Lessons.Single();
            var legacyFolderPath = Path.Combine(Path.GetDirectoryName(courseRoot)!, "Stale Course Root");

            course.CurrentLessonId = lessonId;
            course.FolderPath = legacyFolderPath;
            course.SourceMetadataJson = JsonSerializer.Serialize(
                new CourseSourceMetadata { RootPath = courseRoot },
                WebJsonOptions);
            lesson.Status = LessonStatus.InProgress;
            lesson.WatchedPercentage = watchedPercentage;
            lesson.LastPlaybackPositionSeconds = lastPlaybackPositionSeconds;

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
                await setupContext.Database.ExecuteSqlRawAsync("PRAGMA user_version = 10;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE lessons DROP COLUMN relative_file_path;");
            }

            Assert.False(await ColumnExistsAsync(options, "lessons", "relative_file_path"));
            Assert.False(File.Exists(lessonPath));

            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            var expectedRelativePath = "Modulo 01/Topico 01/Aula 01.mp4";
            await using (var firstAssertContext = new StudyHubDbContext(options))
            {
                var persistedCourse = await firstAssertContext.Courses.SingleAsync(item => item.Id == courseId);
                var persistedLesson = await firstAssertContext.Lessons.SingleAsync(item => item.Id == lessonId);

                Assert.Equal(lessonId, persistedCourse.CurrentLessonId);
                Assert.Equal(legacyFolderPath, persistedCourse.FolderPath);
                Assert.Equal(lessonPath, persistedLesson.FilePath);
                Assert.Equal(lessonPath, persistedLesson.LocalFilePath);
                Assert.Equal(expectedRelativePath, persistedLesson.RelativeFilePath);
                Assert.Equal(LessonStatus.InProgress, persistedLesson.Status);
                Assert.Equal(watchedPercentage, persistedLesson.WatchedPercentage);
                Assert.Equal(lastPlaybackPositionSeconds, persistedLesson.LastPlaybackPositionSeconds);
                Assert.Equal(10, persistedLesson.DurationMinutes);
            }

            Assert.True(await ColumnExistsAsync(options, "lessons", "relative_file_path"));
            Assert.Equal(13, await GetSchemaVersionAsync(options));

            const string preexistingRelativePath = "Already/Preserved.mp4";
            await using (var updateContext = new StudyHubDbContext(options))
            {
                var lessonToPreserve = await updateContext.Lessons.SingleAsync(item => item.Id == lessonId);
                lessonToPreserve.RelativeFilePath = preexistingRelativePath;
                await updateContext.SaveChangesAsync();
            }

            await initializer.InitializeAsync();

            await using var secondAssertContext = new StudyHubDbContext(options);
            var courseAfterSecondInitialization = await secondAssertContext.Courses.SingleAsync(item => item.Id == courseId);
            var lessonAfterSecondInitialization = await secondAssertContext.Lessons.SingleAsync(item => item.Id == lessonId);

            Assert.Equal(lessonId, courseAfterSecondInitialization.CurrentLessonId);
            Assert.Equal(lessonPath, lessonAfterSecondInitialization.FilePath);
            Assert.Equal(lessonPath, lessonAfterSecondInitialization.LocalFilePath);
            Assert.Equal(preexistingRelativePath, lessonAfterSecondInitialization.RelativeFilePath);
            Assert.Equal(LessonStatus.InProgress, lessonAfterSecondInitialization.Status);
            Assert.Equal(watchedPercentage, lessonAfterSecondInitialization.WatchedPercentage);
            Assert.Equal(lastPlaybackPositionSeconds, lessonAfterSecondInitialization.LastPlaybackPositionSeconds);
            Assert.Equal(10, lessonAfterSecondInitialization.DurationMinutes);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_BackfillsRelativeFilePathFromLegacyFilePathWhenLocalFilePathIsEmpty()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var courseRoot = Path.Combine(Path.GetTempPath(), "studyhub-relative-path-tests", Guid.NewGuid().ToString("N"), "Curso B");
        var lessonPath = Path.Combine(courseRoot, "Modulo 02", "Aula 02.mp4");

        try
        {
            var courseId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var course = CreateCourseRecord(
                courseId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                lessonId,
                rootPath: courseRoot,
                lessonPath: lessonPath);
            var lesson = course.Modules.Single().Topics.Single().Lessons.Single();

            course.SourceMetadataJson = "{invalid-json";
            lesson.LocalFilePath = string.Empty;

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var persistedLesson = await assertContext.Lessons.SingleAsync(item => item.Id == lessonId);

            Assert.Equal(lessonPath, persistedLesson.FilePath);
            Assert.Equal(lessonPath, persistedLesson.LocalFilePath);
            Assert.Equal("Modulo 02/Aula 02.mp4", persistedLesson.RelativeFilePath);
            Assert.False(File.Exists(lessonPath));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_DoesNotBackfillRelativeFilePathWhenLessonIsOutsideCourseRoot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var testRoot = Path.Combine(Path.GetTempPath(), "studyhub-relative-path-tests", Guid.NewGuid().ToString("N"));
        var courseRoot = Path.Combine(testRoot, "Curso C");
        var lessonPath = Path.Combine(testRoot, "Outro Lugar", "Aula externa.mp4");

        try
        {
            var courseId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var course = CreateCourseRecord(
                courseId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                lessonId,
                rootPath: courseRoot,
                lessonPath: lessonPath);

            course.SourceMetadataJson = JsonSerializer.Serialize(
                new CourseSourceMetadata { RootPath = courseRoot },
                WebJsonOptions);

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            var initializer = CreateInitializer(options, storageRoot);
            await initializer.InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var persistedLesson = await assertContext.Lessons.SingleAsync(item => item.Id == lessonId);

            Assert.Equal(lessonPath, persistedLesson.FilePath);
            Assert.Equal(lessonPath, persistedLesson.LocalFilePath);
            Assert.Equal(string.Empty, persistedLesson.RelativeFilePath);
            Assert.False(File.Exists(lessonPath));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_NewDatabaseCreatesStructuralSourcePathColumnsWithRequiredEmptyDefaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();

        try
        {
            await CreateInitializer(options, storageRoot).InitializeAsync();

            var moduleColumn = await GetColumnInfoAsync(options, "modules", "source_relative_path");
            var topicColumn = await GetColumnInfoAsync(options, "topics", "source_relative_path");

            Assert.NotNull(moduleColumn);
            Assert.Equal("TEXT", moduleColumn!.StoreType, ignoreCase: true);
            Assert.True(moduleColumn.IsNotNull);
            Assert.Equal("''", moduleColumn.DefaultValue);
            Assert.NotNull(topicColumn);
            Assert.Equal("TEXT", topicColumn!.StoreType, ignoreCase: true);
            Assert.True(topicColumn.IsNotNull);
            Assert.Equal("''", topicColumn.DefaultValue);
            Assert.Equal(13, await GetSchemaVersionAsync(options));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_UpgradesSchema11AndBackfillsStructuralPathsFromMatchingSnapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));
        const string lessonRelativePath = "Modulo 01/Topico 01/Aula 01.mp4";

        try
        {
            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var topicId = Guid.NewGuid();
            var lessonId = Guid.NewGuid();
            var lessonPath = Path.Combine(rootPath, lessonRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var course = CreateCourseRecord(courseId, moduleId, topicId, lessonId, rootPath: rootPath, lessonPath: lessonPath);
            var completedAtUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
            course.CurrentLessonId = lessonId;
            course.Modules.Single().Topics.Single().CompletedAtUtc = completedAtUtc;
            var lesson = course.Modules.Single().Topics.Single().Lessons.Single();
            lesson.RelativeFilePath = lessonRelativePath;
            lesson.Status = LessonStatus.InProgress;
            lesson.WatchedPercentage = 42.5;
            lesson.LastPlaybackPositionSeconds = 87;

            var snapshot = CreateSnapshot(
                course,
                "Modulo 01",
                "Topico 01",
                lessonRelativePath);

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                setupContext.CourseImportSnapshots.Add(snapshot);
                await setupContext.SaveChangesAsync();
                await setupContext.Database.ExecuteSqlRawAsync("PRAGMA user_version = 11;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE topics DROP COLUMN source_relative_path;");
                await setupContext.Database.ExecuteSqlRawAsync("ALTER TABLE modules DROP COLUMN source_relative_path;");
            }

            var initializer = CreateInitializer(options, storageRoot);
            Assert.False(File.Exists(lessonPath));
            await initializer.InitializeAsync();

            await using (var firstAssertContext = new StudyHubDbContext(options))
            {
                var persistedCourse = await firstAssertContext.Courses
                    .Include(item => item.Modules)
                        .ThenInclude(item => item.Topics)
                            .ThenInclude(item => item.Lessons)
                    .SingleAsync(item => item.Id == courseId);
                var persistedModule = await firstAssertContext.Modules.SingleAsync(item => item.Id == moduleId);
                var persistedTopic = await firstAssertContext.Topics.SingleAsync(item => item.Id == topicId);
                var persistedLesson = await firstAssertContext.Lessons.SingleAsync(item => item.Id == lessonId);

                Assert.Equal(courseId, persistedCourse.Id);
                Assert.Equal(lessonId, persistedCourse.CurrentLessonId);
                Assert.Single(persistedCourse.Modules);
                Assert.Single(persistedCourse.Modules.Single().Topics);
                Assert.Single(persistedCourse.Modules.Single().Topics.Single().Lessons);
                Assert.Equal(moduleId, persistedModule.Id);
                Assert.Equal("Modulo 01", persistedModule.SourceRelativePath);
                Assert.Equal(topicId, persistedTopic.Id);
                Assert.Equal("Modulo 01/Topico 01", persistedTopic.SourceRelativePath);
                Assert.Equal(completedAtUtc, persistedTopic.CompletedAtUtc);
                Assert.Equal(lessonId, persistedLesson.Id);
                Assert.Equal(lessonRelativePath, persistedLesson.RelativeFilePath);
                Assert.Equal(LessonStatus.InProgress, persistedLesson.Status);
                Assert.Equal(42.5, persistedLesson.WatchedPercentage);
                Assert.Equal(87, persistedLesson.LastPlaybackPositionSeconds);
            }

            Assert.Equal(13, await GetSchemaVersionAsync(options));

            await using (var updateContext = new StudyHubDbContext(options))
            {
                var persistedModule = await updateContext.Modules.SingleAsync(item => item.Id == moduleId);
                var persistedTopic = await updateContext.Topics.SingleAsync(item => item.Id == topicId);
                persistedModule.SourceRelativePath = @"Already\Preserved";
                persistedTopic.SourceRelativePath = @"Already\Preserved\Topic";
                await updateContext.SaveChangesAsync();
            }

            await initializer.InitializeAsync();

            await using var secondAssertContext = new StudyHubDbContext(options);
            var moduleAfterSecondRun = await secondAssertContext.Modules.SingleAsync(item => item.Id == moduleId);
            var topicAfterSecondRun = await secondAssertContext.Topics.SingleAsync(item => item.Id == topicId);
            var lessonAfterSecondRun = await secondAssertContext.Lessons.SingleAsync(item => item.Id == lessonId);
            var courseAfterSecondRun = await secondAssertContext.Courses.SingleAsync(item => item.Id == courseId);

            Assert.Equal("Already/Preserved", moduleAfterSecondRun.SourceRelativePath);
            Assert.Equal("Already/Preserved/Topic", topicAfterSecondRun.SourceRelativePath);
            Assert.Equal(completedAtUtc, topicAfterSecondRun.CompletedAtUtc);
            Assert.Equal(lessonId, courseAfterSecondRun.CurrentLessonId);
            Assert.Equal(LessonStatus.InProgress, lessonAfterSecondRun.Status);
            Assert.Equal(42.5, lessonAfterSecondRun.WatchedPercentage);
            Assert.Equal(87, lessonAfterSecondRun.LastPlaybackPositionSeconds);

            await initializer.InitializeAsync();

            await using var thirdAssertContext = new StudyHubDbContext(options);
            Assert.Equal(
                "Already/Preserved",
                (await thirdAssertContext.Modules.SingleAsync(item => item.Id == moduleId)).SourceRelativePath);
            Assert.Equal(
                "Already/Preserved/Topic",
                (await thirdAssertContext.Topics.SingleAsync(item => item.Id == topicId)).SourceRelativePath);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_InvalidSnapshotFallsBackToDirectLessonDirectory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));
        const string lessonRelativePath = "Modulo 01/Aula 01.mp4";

        try
        {
            var course = CreateCourseRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rootPath: rootPath,
                lessonPath: Path.Combine(rootPath, "Modulo 01", "Aula 01.mp4"));
            course.Modules.Single().Topics.Single().Lessons.Single().RelativeFilePath = lessonRelativePath;

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                setupContext.CourseImportSnapshots.Add(new CourseImportSnapshotRecord
                {
                    CourseId = course.Id,
                    SourceKind = "local-folder",
                    RootFolderPath = rootPath,
                    StructureJson = "{invalid-json",
                    ImportedAt = DateTime.UtcNow
                });
                await setupContext.SaveChangesAsync();
            }

            await CreateInitializer(options, storageRoot).InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var module = await assertContext.Modules.SingleAsync(item => item.CourseId == course.Id);
            var topic = await assertContext.Topics.SingleAsync(item => item.ModuleId == module.Id);

            Assert.Equal("Modulo 01", module.SourceRelativePath);
            Assert.Equal("Modulo 01", topic.SourceRelativePath);
            Assert.Equal(13, await GetSchemaVersionAsync(options));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_SnapshotLessonPathMismatchIsRejectedBeforeFallback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));
        const string persistedRelativePath = "Modulo 01/Aula 01.mp4";

        try
        {
            var course = CreateCourseRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rootPath: rootPath,
                lessonPath: Path.Combine(rootPath, "Modulo 01", "Aula 01.mp4"));
            course.Modules.Single().Topics.Single().Lessons.Single().RelativeFilePath = persistedRelativePath;
            var corruptSnapshot = CreateSnapshot(
                course,
                "Outro Modulo",
                ".",
                "Outro Modulo/Aula 01.mp4");

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                setupContext.CourseImportSnapshots.Add(corruptSnapshot);
                await setupContext.SaveChangesAsync();
            }

            await CreateInitializer(options, storageRoot).InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var module = await assertContext.Modules.SingleAsync(item => item.CourseId == course.Id);
            var topic = await assertContext.Topics.SingleAsync(item => item.ModuleId == module.Id);

            Assert.Equal("Modulo 01", module.SourceRelativePath);
            Assert.Equal("Modulo 01", topic.SourceRelativePath);
            Assert.NotEqual("Outro Modulo", module.SourceRelativePath);
            Assert.NotEqual("Outro Modulo", topic.SourceRelativePath);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_FallbackInfersTopicsAndNonRootCommonModuleAncestor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var course = CreateCourseRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rootPath: rootPath,
                lessonPath: Path.Combine(rootPath, "Modulo 01", "Topico A", "Aula A.mp4"));
            var module = course.Modules.Single();
            var firstTopic = module.Topics.Single();
            firstTopic.Lessons.Single().RelativeFilePath = "Modulo 01/Topico A/Aula A.mp4";
            var secondTopicId = Guid.NewGuid();
            module.Topics.Add(new TopicRecord
            {
                Id = secondTopicId,
                ModuleId = module.Id,
                Order = 2,
                RawTitle = "Topico B",
                Title = "Topico B",
                Lessons =
                [
                    new LessonRecord
                    {
                        Id = Guid.NewGuid(),
                        TopicId = secondTopicId,
                        Order = 1,
                        RawTitle = "Aula B",
                        Title = "Aula B",
                        FilePath = Path.Combine(rootPath, "Modulo 01", "Topico B", "Aula B.mp4"),
                        LocalFilePath = Path.Combine(rootPath, "Modulo 01", "Topico B", "Aula B.mp4"),
                        RelativeFilePath = "Modulo 01/Topico B/Aula B.mp4",
                        Provider = "LocalFileSystem"
                    }
                ]
            });

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            await CreateInitializer(options, storageRoot).InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var persistedModule = await assertContext.Modules
                .Include(item => item.Topics)
                .SingleAsync(item => item.Id == module.Id);

            Assert.Equal("Modulo 01", persistedModule.SourceRelativePath);
            Assert.Equal(
                ["Modulo 01/Topico A", "Modulo 01/Topico B"],
                persistedModule.Topics.OrderBy(item => item.Order).Select(item => item.SourceRelativePath).ToArray());
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_SingleNestedTopicDoesNotInventAmbiguousModuleAncestor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));
        const string lessonRelativePath = "Modulo 01/Topico 01/Aula.mp4";

        try
        {
            var course = CreateCourseRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rootPath: rootPath,
                lessonPath: Path.Combine(rootPath, "Modulo 01", "Topico 01", "Aula.mp4"));
            course.Modules.Single().Topics.Single().Lessons.Single().RelativeFilePath = lessonRelativePath;

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            await CreateInitializer(options, storageRoot).InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var module = await assertContext.Modules.SingleAsync(item => item.CourseId == course.Id);
            var topic = await assertContext.Topics.SingleAsync(item => item.ModuleId == module.Id);

            Assert.Equal(string.Empty, module.SourceRelativePath);
            Assert.Equal("Modulo 01/Topico 01", topic.SourceRelativePath);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_RootLessonFallbackUsesDotForModuleAndTopic()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var course = CreateCourseRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rootPath: rootPath,
                lessonPath: Path.Combine(rootPath, "Aula.mp4"));
            course.Modules.Single().Topics.Single().Lessons.Single().RelativeFilePath = "Aula.mp4";

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            await CreateInitializer(options, storageRoot).InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var module = await assertContext.Modules.SingleAsync(item => item.CourseId == course.Id);
            var topic = await assertContext.Topics.SingleAsync(item => item.ModuleId == module.Id);

            Assert.Equal(".", module.SourceRelativePath);
            Assert.Equal(".", topic.SourceRelativePath);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    [Fact]
    public async Task InitializeAsync_AmbiguousLessonDirectoriesLeaveStructuralPathsEmpty()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var storageRoot = CreateStorageRoot();
        var rootPath = Path.Combine(Path.GetTempPath(), "studyhub-structural-path-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var course = CreateCourseRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rootPath: rootPath,
                lessonPath: Path.Combine(rootPath, "Area A", "Aula 01.mp4"));
            var topic = course.Modules.Single().Topics.Single();
            topic.Lessons.Single().RelativeFilePath = "Area A/Aula 01.mp4";
            topic.Lessons.Add(new LessonRecord
            {
                Id = Guid.NewGuid(),
                TopicId = topic.Id,
                Order = 2,
                RawTitle = "Aula 02",
                Title = "Aula 02",
                FilePath = Path.Combine(rootPath, "Area B", "Aula 02.mp4"),
                LocalFilePath = Path.Combine(rootPath, "Area B", "Aula 02.mp4"),
                RelativeFilePath = "Area B/Aula 02.mp4",
                Provider = "LocalFileSystem"
            });

            await using (var setupContext = new StudyHubDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();
                setupContext.Courses.Add(course);
                await setupContext.SaveChangesAsync();
            }

            await CreateInitializer(options, storageRoot).InitializeAsync();

            await using var assertContext = new StudyHubDbContext(options);
            var module = await assertContext.Modules.SingleAsync(item => item.CourseId == course.Id);
            var persistedTopic = await assertContext.Topics.SingleAsync(item => item.Id == topic.Id);

            Assert.Equal(string.Empty, module.SourceRelativePath);
            Assert.Equal(string.Empty, persistedTopic.SourceRelativePath);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    private static DbContextOptions<StudyHubDbContext> CreateOptions(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    private static StudyHubDatabaseInitializer CreateInitializer(
        DbContextOptions<StudyHubDbContext> options,
        string storageRoot)
    {
        return new StudyHubDatabaseInitializer(
            new TestDbContextFactory(options),
            new TestStoragePathsService(storageRoot),
            NullLogger<StudyHubDatabaseInitializer>.Instance);
    }

    private static CourseImportSnapshotRecord CreateSnapshot(
        CourseRecord course,
        string moduleRelativePath,
        string topicRelativePath,
        string lessonRelativePath)
    {
        var module = course.Modules.Single();
        var topic = module.Topics.Single();
        var lesson = topic.Lessons.Single();
        var manifest = new DetectedCourseStructure
        {
            CourseId = course.Id,
            RootFolderName = Path.GetFileName(course.FolderPath),
            RootFolderPath = course.FolderPath,
            ScannedAt = DateTime.UtcNow,
            Modules =
            [
                new DetectedModuleStructure
                {
                    ModuleId = module.Id,
                    RelativePath = moduleRelativePath,
                    Topics =
                    [
                        new DetectedTopicStructure
                        {
                            TopicId = topic.Id,
                            RelativePath = topicRelativePath,
                            Lessons =
                            [
                                new DetectedLessonFile
                                {
                                    LessonId = lesson.Id,
                                    FileName = Path.GetFileName(lesson.LocalFilePath),
                                    RelativePath = lessonRelativePath,
                                    AbsolutePath = lesson.LocalFilePath
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        return new CourseImportSnapshotRecord
        {
            CourseId = course.Id,
            SourceKind = "local-folder",
            RootFolderPath = course.FolderPath,
            StructureJson = JsonSerializer.Serialize(manifest, WebJsonOptions),
            ImportedAt = DateTime.UtcNow
        };
    }

    private static CourseRecord CreateCourseRecord(
        Guid courseId,
        Guid moduleId,
        Guid topicId,
        Guid lessonId,
        DateTime? completedAtUtc = null,
        string? rootPath = null,
        string? lessonPath = null)
    {
        rootPath ??= @"C:\courses\topic-completion";
        lessonPath ??= Path.Combine(rootPath, "video.mp4");

        return new CourseRecord
        {
            Id = courseId,
            RawTitle = "Curso",
            RawDescription = "Descricao do curso",
            Title = "Curso",
            Description = "Descricao do curso",
            Category = "Curso Local",
            ThumbnailUrl = string.Empty,
            FolderPath = rootPath,
            SourceType = CourseSourceType.LocalFolder,
            LifecycleStatus = CourseLifecycleStatus.Active,
            SourceMetadataJson = "{}",
            TotalDurationMinutes = 10,
            AddedAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            Modules =
            [
                new ModuleRecord
                {
                    Id = moduleId,
                    CourseId = courseId,
                    Order = 1,
                    RawTitle = "Modulo",
                    RawDescription = "Descricao do modulo",
                    Title = "Modulo",
                    Description = "Descricao do modulo",
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = topicId,
                            ModuleId = moduleId,
                            Order = 1,
                            RawTitle = "Aula 01",
                            RawDescription = "Descricao da aula",
                            Title = "Aula 01",
                            Description = "Descricao da aula",
                            CompletedAtUtc = completedAtUtc,
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = lessonId,
                                    TopicId = topicId,
                                    Order = 1,
                                    RawTitle = "Video 01",
                                    RawDescription = "Descricao do video",
                                    Title = "Video 01",
                                    Description = "Descricao do video",
                                    FilePath = lessonPath,
                                    LocalFilePath = lessonPath,
                                    RelativeFilePath = string.Empty,
                                    Provider = "LocalFileSystem",
                                    DurationMinutes = 10
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static async Task AssertAvailabilityColumnAsync(
        DbContextOptions<StudyHubDbContext> options,
        string tableName)
    {
        var column = await GetColumnInfoAsync(options, tableName, "is_available");

        Assert.NotNull(column);
        Assert.Equal("INTEGER", column!.StoreType, ignoreCase: true);
        Assert.True(column.IsNotNull);
        Assert.Equal("1", column.DefaultValue);
    }

    private static void AssertAvailabilityUpgradeState(
        CourseRecord persisted,
        Guid moduleId,
        Guid topicId,
        Guid lessonId,
        DateTime completedAtUtc,
        bool expectedAvailability)
    {
        var module = Assert.Single(persisted.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);

        Assert.Equal(moduleId, module.Id);
        Assert.Equal(topicId, topic.Id);
        Assert.Equal(lessonId, lesson.Id);
        Assert.Equal(lessonId, persisted.CurrentLessonId);
        Assert.Equal("Modulo", module.Title);
        Assert.Equal("Modulo 01", module.SourceRelativePath);
        Assert.Equal("Aula 01", topic.Title);
        Assert.Equal("Modulo 01/Topico 01", topic.SourceRelativePath);
        Assert.Equal(completedAtUtc, topic.CompletedAtUtc);
        Assert.Equal("Video 01", lesson.Title);
        Assert.Equal("Modulo 01/Topico 01/Aula 01.mp4", lesson.RelativeFilePath);
        Assert.Equal(10, lesson.DurationMinutes);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(46.5, lesson.WatchedPercentage);
        Assert.Equal(91, lesson.LastPlaybackPositionSeconds);
        Assert.Equal(expectedAvailability, module.IsAvailable);
        Assert.Equal(expectedAvailability, topic.IsAvailable);
        Assert.Equal(expectedAvailability, lesson.IsAvailable);
    }

    private static async Task<bool> ColumnExistsAsync(
        DbContextOptions<StudyHubDbContext> options,
        string tableName,
        string columnName)
    {
        await using var context = new StudyHubDbContext(options);
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<SqliteColumnInfo?> GetColumnInfoAsync(
        DbContextOptions<StudyHubDbContext> options,
        string tableName,
        string columnName)
    {
        await using var context = new StudyHubDbContext(options);
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new SqliteColumnInfo(
                reader.GetString(2),
                reader.GetInt64(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        return null;
    }

    private static async Task<int> GetSchemaVersionAsync(DbContextOptions<StudyHubDbContext> options)
    {
        await using var context = new StudyHubDbContext(options);
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        var result = await command.ExecuteScalarAsync();
        return result switch
        {
            long longValue => checked((int)longValue),
            int intValue => intValue,
            _ => 0
        };
    }

    private static string CreateStorageRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "studyhub-database-initializer-tests",
            Guid.NewGuid().ToString("N"));
    }

    private static void DeleteStorageRoot(string storageRoot)
    {
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
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

    private sealed record SqliteColumnInfo(string StoreType, bool IsNotNull, string? DefaultValue);
}
