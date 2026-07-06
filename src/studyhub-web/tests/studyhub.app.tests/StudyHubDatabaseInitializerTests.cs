using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class StudyHubDatabaseInitializerTests
{
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
            Assert.Equal(10, await GetSchemaVersionAsync(options));

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
            Assert.Equal(10, await GetSchemaVersionAsync(options));

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
            Assert.Equal(10, await GetSchemaVersionAsync(options));
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

    private static CourseRecord CreateCourseRecord(
        Guid courseId,
        Guid moduleId,
        Guid topicId,
        Guid lessonId,
        DateTime? completedAtUtc = null)
    {
        return new CourseRecord
        {
            Id = courseId,
            RawTitle = "Curso",
            RawDescription = "Descricao do curso",
            Title = "Curso",
            Description = "Descricao do curso",
            Category = "Curso Local",
            ThumbnailUrl = string.Empty,
            FolderPath = @"C:\courses\topic-completion",
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
                                    FilePath = @"C:\courses\topic-completion\video.mp4",
                                    LocalFilePath = @"C:\courses\topic-completion\video.mp4",
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
}
