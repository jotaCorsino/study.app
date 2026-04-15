using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class RoutineServiceTests : IDisposable
{
    private readonly string _rootDirectory;

    public RoutineServiceTests()
    {
        _rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "studyhub-routine-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task GetMonthlyRecordsAsync_IgnoresDaysBeforeCourseAddedAt()
    {
        var courseId = Guid.NewGuid();
        var courseAddedAt = new DateTime(2026, 4, 15, 10, 0, 0);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "Curso",
                RawDescription = "Descricao",
                Title = "Curso",
                Description = "Descricao",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = string.Empty,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 0,
                AddedAt = courseAddedAt
            });
            await setupContext.SaveChangesAsync();
        }

        var storage = new TestStoragePathsService(_rootDirectory);
        var service = new RoutineService(storage, new TestDbContextFactory(options));

        await WriteSettingsAsync(storage, courseId, new RoutineSettings
        {
            DailyGoalMinutes = 30,
            SelectedDaysOfWeek =
            [
                DayOfWeek.Sunday,
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday
            ],
            LastUpdatedAt = courseAddedAt.Date
        });

        var records = await service.GetMonthlyRecordsAsync(courseId, 2026, 4);

        var beforeCreation = Assert.Single(records.Where(record => record.Date == new DateTime(2026, 4, 14)));
        Assert.Equal(DailyStudyStatus.Unplanned, beforeCreation.Status);
        Assert.Equal(0, beforeCreation.DailyGoalMinutesAtTheTime);

        var creationDay = Assert.Single(records.Where(record => record.Date == new DateTime(2026, 4, 15)));
        Assert.Equal(DailyStudyStatus.NotStarted, creationDay.Status);
        Assert.Equal(30, creationDay.DailyGoalMinutesAtTheTime);
    }

    [Fact]
    public async Task GetMonthlyRecordsAsync_FallsBackToFirstRecordedDate_WhenCourseAddedAtIsMissing()
    {
        var courseId = Guid.NewGuid();
        var firstRecordedDate = new DateTime(2026, 4, 10);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.Add(new CourseRecord
            {
                Id = courseId,
                RawTitle = "Curso Legado",
                RawDescription = "Descricao",
                Title = "Curso Legado",
                Description = "Descricao",
                Category = "Curso Local",
                ThumbnailUrl = string.Empty,
                FolderPath = string.Empty,
                SourceMetadataJson = "{}",
                TotalDurationMinutes = 0,
                AddedAt = default
            });
            await setupContext.SaveChangesAsync();
        }

        var storage = new TestStoragePathsService(_rootDirectory);
        var service = new RoutineService(storage, new TestDbContextFactory(options));

        await WriteSettingsAsync(storage, courseId, new RoutineSettings
        {
            DailyGoalMinutes = 30,
            SelectedDaysOfWeek =
            [
                DayOfWeek.Sunday,
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday
            ],
            LastUpdatedAt = new DateTime(2026, 4, 1)
        });

        await WriteRecordsAsync(storage, courseId,
        [
            new DailyStudyRecord
            {
                CourseId = courseId,
                Date = firstRecordedDate,
                NonLessonMinutesStudied = 30,
                MinutesStudied = 30,
                DailyGoalMinutesAtTheTime = 30,
                Status = DailyStudyStatus.Completed
            }
        ]);

        var records = await service.GetMonthlyRecordsAsync(courseId, 2026, 4);

        var beforeFirstRecord = Assert.Single(records.Where(record => record.Date == new DateTime(2026, 4, 9)));
        Assert.Equal(DailyStudyStatus.Unplanned, beforeFirstRecord.Status);
        Assert.Equal(0, beforeFirstRecord.DailyGoalMinutesAtTheTime);

        var preservedRecord = Assert.Single(records.Where(record => record.Date == firstRecordedDate));
        Assert.Equal(DailyStudyStatus.Completed, preservedRecord.Status);
        Assert.Equal(30, preservedRecord.MinutesStudied);
        Assert.Equal(30, preservedRecord.DailyGoalMinutesAtTheTime);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static async Task WriteSettingsAsync(TestStoragePathsService storage, Guid courseId, RoutineSettings settings)
    {
        var courseDirectory = EnsureCourseDirectory(storage, courseId);
        var path = Path.Combine(courseDirectory, "routine_settings.json");
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task WriteRecordsAsync(
        TestStoragePathsService storage,
        Guid courseId,
        IReadOnlyCollection<DailyStudyRecord> records)
    {
        var courseDirectory = EnsureCourseDirectory(storage, courseId);
        var path = Path.Combine(courseDirectory, "daily_records.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static string EnsureCourseDirectory(TestStoragePathsService storage, Guid courseId)
    {
        var courseDirectory = Path.Combine(storage.RoutineDirectory, courseId.ToString());
        Directory.CreateDirectory(courseDirectory);
        return courseDirectory;
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudyHubDbContext> options) : IDbContextFactory<StudyHubDbContext>
    {
        private readonly DbContextOptions<StudyHubDbContext> _options = options;

        public StudyHubDbContext CreateDbContext()
            => new(_options);

        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new StudyHubDbContext(_options));
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
}
