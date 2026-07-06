using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class PersistedProgressServiceTests : IDisposable
{
    private readonly string _rootDirectory;

    public PersistedProgressServiceTests()
    {
        _rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "studyhub-progress-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_DoesNotCompleteTopic_WhenOtherLessonsRemainIncomplete()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var firstLessonId = Guid.NewGuid();
        var secondLessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId,
            CreateLesson(firstLessonId),
            CreateLesson(secondLessonId))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, firstLessonId);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.Null(topic.CompletedAtUtc);
        Assert.Empty(record.CompletedStudyUnitIds);
        var lessonCredit = Assert.Single(record.LessonCredits);
        Assert.Equal(firstLessonId, lessonCredit.LessonId);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_CompletesTopicAndCreditsStudyUnit_WhenLastLessonIsCompleted()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var firstLessonId = Guid.NewGuid();
        var lastLessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId,
            CreateLesson(firstLessonId, LessonStatus.Completed),
            CreateLesson(lastLessonId))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, lastLessonId);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.NotNull(topic.CompletedAtUtc);
        Assert.Equal(DateTimeKind.Utc, topic.CompletedAtUtc!.Value.Kind);
        Assert.Equal(ResolveExpectedRoutineDate(topic.CompletedAtUtc.Value), record.Date);
        Assert.Equal([topicId], record.CompletedStudyUnitIds);
        Assert.Equal(1, record.CompletedStudyUnitCount);
    }

    [Fact]
    public async Task UpdateLessonPlaybackAsync_CompletesTopicAndCreditsStudyUnit_WhenPlaybackCompletesLastLesson()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var firstLessonId = Guid.NewGuid();
        var lastLessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId,
            CreateLesson(firstLessonId, LessonStatus.Completed),
            CreateLesson(lastLessonId))));

        var service = CreateProgressService(options, out var storage);

        await service.UpdateLessonPlaybackAsync(
            courseId,
            lastLessonId,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10),
            markAsCompleted: true);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.NotNull(topic.CompletedAtUtc);
        Assert.Equal(DateTimeKind.Utc, topic.CompletedAtUtc!.Value.Kind);
        Assert.Equal(ResolveExpectedRoutineDate(topic.CompletedAtUtc.Value), record.Date);
        Assert.Equal([topicId], record.CompletedStudyUnitIds);
        Assert.Equal(1, record.CompletedStudyUnitCount);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_IsIdempotentForAlreadyCompletedTopic()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId, CreateLesson(lessonId))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, lessonId);
        var firstCompletedAtUtc = (await LoadTopicAsync(options, topicId)).CompletedAtUtc;

        await service.MarkLessonCompletedAsync(courseId, lessonId);
        var secondCompletedAtUtc = (await LoadTopicAsync(options, topicId)).CompletedAtUtc;
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.NotNull(firstCompletedAtUtc);
        Assert.Equal(firstCompletedAtUtc, secondCompletedAtUtc);
        Assert.Equal([topicId], record.CompletedStudyUnitIds);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_CreditsTwoDistinctTopicsOnSameDay()
    {
        var courseId = Guid.NewGuid();
        var firstTopicId = Guid.NewGuid();
        var secondTopicId = Guid.NewGuid();
        var firstLessonId = Guid.NewGuid();
        var secondLessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId,
            CreateTopic(firstTopicId, CreateLesson(firstLessonId)),
            CreateTopic(secondTopicId, CreateLesson(secondLessonId))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, firstLessonId);
        await service.MarkLessonCompletedAsync(courseId, secondLessonId);

        var firstTopic = await LoadTopicAsync(options, firstTopicId);
        var secondTopic = await LoadTopicAsync(options, secondTopicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.NotNull(firstTopic.CompletedAtUtc);
        Assert.NotNull(secondTopic.CompletedAtUtc);
        Assert.Equal(2, record.CompletedStudyUnitCount);
        Assert.Contains(firstTopicId, record.CompletedStudyUnitIds);
        Assert.Contains(secondTopicId, record.CompletedStudyUnitIds);
    }

    [Fact]
    public async Task GetProgressByCourseAsync_DoesNotCompleteOrCreditTopicWithoutLessons()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId)));

        var service = CreateProgressService(options, out var storage);

        var progress = await service.GetProgressByCourseAsync(courseId);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);

        Assert.NotNull(progress);
        Assert.Equal(0, progress!.TotalLessons);
        Assert.Null(topic.CompletedAtUtc);
        Assert.Empty(records);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_DoesNotBackfillCompletedLegacyTopicWithoutTimestamp()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId,
            CreateLesson(lessonId, LessonStatus.Completed))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, lessonId);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);

        Assert.Null(topic.CompletedAtUtc);
        Assert.Empty(records);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_ReconcilesRoutineCreditUsingOriginalTopicCompletionDate()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var completedAtUtc = new DateTime(2026, 7, 6, 15, 0, 0, DateTimeKind.Utc);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(
            topicId,
            completedAtUtc,
            CreateLesson(lessonId, LessonStatus.Completed))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, lessonId);
        await service.MarkLessonCompletedAsync(courseId, lessonId);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.Equal(completedAtUtc, topic.CompletedAtUtc);
        Assert.Equal(ResolveExpectedRoutineDate(completedAtUtc), record.Date);
        Assert.Equal([topicId], record.CompletedStudyUnitIds);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_ReconcilesRoutineCreditOnLocalDate_WhenUtcCompletionFallsOnPreviousLocalDay()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var completedAtUtc = new DateTime(2026, 7, 7, 1, 30, 0, DateTimeKind.Utc);
        var expectedLocalDate = new DateTime(2026, 7, 6);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(
            topicId,
            completedAtUtc,
            CreateLesson(lessonId, LessonStatus.Completed))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, lessonId);
        await service.MarkLessonCompletedAsync(courseId, lessonId);

        var topic = await LoadTopicAsync(options, topicId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);

        Assert.Equal(completedAtUtc, topic.CompletedAtUtc);
        Assert.Equal(expectedLocalDate, ResolveExpectedRoutineDate(completedAtUtc));
        Assert.NotEqual(completedAtUtc.Date, record.Date);
        Assert.Equal(expectedLocalDate, record.Date);
        Assert.Equal([topicId], record.CompletedStudyUnitIds);
        Assert.Equal(1, record.CompletedStudyUnitCount);
    }

    [Fact]
    public async Task MarkLessonCompletedAsync_PreservesLessonCompletionMinuteCreditAndCourseProgress()
    {
        var courseId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);
        await SeedCourseAsync(options, CreateCourse(courseId, CreateTopic(topicId, CreateLesson(lessonId, durationMinutes: 12))));

        var service = CreateProgressService(options, out var storage);

        await service.MarkLessonCompletedAsync(courseId, lessonId);

        await using var context = new StudyHubDbContext(options);
        var lesson = await context.Lessons.SingleAsync(item => item.Id == lessonId);
        var course = await context.Courses.SingleAsync(item => item.Id == courseId);
        var progress = await service.GetProgressByCourseAsync(courseId);
        var records = await ReadRoutineRecordsAsync(storage, courseId);
        var record = Assert.Single(records);
        var lessonCredit = Assert.Single(record.LessonCredits);

        Assert.Equal(LessonStatus.Completed, lesson.Status);
        Assert.Equal(100, lesson.WatchedPercentage);
        Assert.Equal(720, lesson.LastPlaybackPositionSeconds);
        Assert.Equal(lessonId, course.CurrentLessonId);
        Assert.NotNull(course.LastAccessedAt);
        Assert.Equal(lessonId, lessonCredit.LessonId);
        Assert.Equal(12, lessonCredit.MinutesCredited);
        Assert.Equal([topicId], record.CompletedStudyUnitIds);
        Assert.NotNull(progress);
        Assert.Equal(1, progress!.CompletedLessons);
        Assert.Equal(1, progress.TotalLessons);
        Assert.Equal(100, progress.OverallPercentage);
    }

    private PersistedProgressService CreateProgressService(
        DbContextOptions<StudyHubDbContext> options,
        out TestStoragePathsService storage)
    {
        storage = new TestStoragePathsService(_rootDirectory);
        var contextFactory = new TestDbContextFactory(options);
        var routineService = new RoutineService(storage, contextFactory);
        return new PersistedProgressService(contextFactory, routineService);
    }

    private static DbContextOptions<StudyHubDbContext> CreateOptions(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    private static async Task SeedCourseAsync(DbContextOptions<StudyHubDbContext> options, CourseRecord course)
    {
        await using var context = new StudyHubDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Courses.Add(course);
        await context.SaveChangesAsync();
    }

    private static CourseRecord CreateCourse(Guid courseId, params TopicRecord[] topics)
    {
        var moduleId = Guid.NewGuid();
        foreach (var topic in topics)
        {
            topic.ModuleId = moduleId;
            foreach (var lesson in topic.Lessons)
            {
                lesson.TopicId = topic.Id;
            }
        }

        return new CourseRecord
        {
            Id = courseId,
            RawTitle = "Curso",
            RawDescription = "Descricao do curso",
            Title = "Curso",
            Description = "Descricao do curso",
            Category = "Curso Local",
            ThumbnailUrl = string.Empty,
            FolderPath = @"C:\courses\progress",
            SourceType = CourseSourceType.LocalFolder,
            LifecycleStatus = CourseLifecycleStatus.Active,
            SourceMetadataJson = "{}",
            TotalDurationMinutes = topics
                .SelectMany(topic => topic.Lessons)
                .Sum(lesson => lesson.DurationMinutes),
            AddedAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Local),
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
                    Topics = topics.ToList()
                }
            ]
        };
    }

    private static TopicRecord CreateTopic(Guid topicId, params LessonRecord[] lessons)
        => CreateTopic(topicId, null, lessons);

    private static TopicRecord CreateTopic(Guid topicId, DateTime? completedAtUtc, params LessonRecord[] lessons)
    {
        return new TopicRecord
        {
            Id = topicId,
            Order = 1,
            RawTitle = "Aula",
            RawDescription = "Descricao da aula",
            Title = "Aula",
            Description = "Descricao da aula",
            CompletedAtUtc = completedAtUtc,
            Lessons = lessons.ToList()
        };
    }

    private static LessonRecord CreateLesson(
        Guid lessonId,
        LessonStatus status = LessonStatus.NotStarted,
        int durationMinutes = 10)
    {
        return new LessonRecord
        {
            Id = lessonId,
            Order = 1,
            RawTitle = "Video",
            RawDescription = "Descricao do video",
            Title = "Video",
            Description = "Descricao do video",
            FilePath = $@"C:\courses\progress\{lessonId:N}.mp4",
            SourceType = LessonSourceType.LocalFile,
            LocalFilePath = $@"C:\courses\progress\{lessonId:N}.mp4",
            Provider = "LocalFileSystem",
            DurationMinutes = durationMinutes,
            Status = status,
            WatchedPercentage = status == LessonStatus.Completed ? 100 : 0,
            LastPlaybackPositionSeconds = status == LessonStatus.Completed
                ? (int)TimeSpan.FromMinutes(durationMinutes).TotalSeconds
                : 0
        };
    }

    private static async Task<TopicRecord> LoadTopicAsync(DbContextOptions<StudyHubDbContext> options, Guid topicId)
    {
        await using var context = new StudyHubDbContext(options);
        return await context.Topics
            .Include(topic => topic.Lessons)
            .SingleAsync(topic => topic.Id == topicId);
    }

    private static async Task<List<DailyStudyRecord>> ReadRoutineRecordsAsync(
        TestStoragePathsService storage,
        Guid courseId)
    {
        var path = Path.Combine(storage.RoutineDirectory, courseId.ToString(), "daily_records.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<DailyStudyRecord>>(json) ?? [];
    }

    private static DateTime ResolveExpectedRoutineDate(DateTime completedAtUtc)
    {
        var normalizedCompletedAtUtc = completedAtUtc.Kind switch
        {
            DateTimeKind.Utc => completedAtUtc,
            DateTimeKind.Local => completedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc)
        };

        return normalizedCompletedAtUtc.ToLocalTime().Date;
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
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
