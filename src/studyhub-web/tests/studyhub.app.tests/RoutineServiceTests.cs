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

    [Fact]
    public async Task GetDailyRecordsAsync_ReturnsBatchIndicatorsForMultipleCourses()
    {
        var partialCourseId = Guid.NewGuid();
        var notStartedCourseId = Guid.NewGuid();
        var notCreatedYetCourseId = Guid.NewGuid();
        var targetDate = new DateTime(2026, 4, 15);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Courses.AddRange(
                new CourseRecord
                {
                    Id = partialCourseId,
                    RawTitle = "Curso 1",
                    RawDescription = "Descricao",
                    Title = "Curso 1",
                    Description = "Descricao",
                    Category = "Curso Local",
                    ThumbnailUrl = string.Empty,
                    FolderPath = string.Empty,
                    SourceMetadataJson = "{}",
                    TotalDurationMinutes = 0,
                    AddedAt = new DateTime(2026, 4, 1)
                },
                new CourseRecord
                {
                    Id = notStartedCourseId,
                    RawTitle = "Curso 2",
                    RawDescription = "Descricao",
                    Title = "Curso 2",
                    Description = "Descricao",
                    Category = "Curso Local",
                    ThumbnailUrl = string.Empty,
                    FolderPath = string.Empty,
                    SourceMetadataJson = "{}",
                    TotalDurationMinutes = 0,
                    AddedAt = new DateTime(2026, 4, 1)
                },
                new CourseRecord
                {
                    Id = notCreatedYetCourseId,
                    RawTitle = "Curso 3",
                    RawDescription = "Descricao",
                    Title = "Curso 3",
                    Description = "Descricao",
                    Category = "Curso Local",
                    ThumbnailUrl = string.Empty,
                    FolderPath = string.Empty,
                    SourceMetadataJson = "{}",
                    TotalDurationMinutes = 0,
                    AddedAt = new DateTime(2026, 4, 20)
                });
            await setupContext.SaveChangesAsync();
        }

        var storage = new TestStoragePathsService(_rootDirectory);
        var service = new RoutineService(storage, new TestDbContextFactory(options));

        var plannedSettings = new RoutineSettings
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
        };

        await WriteSettingsAsync(storage, partialCourseId, plannedSettings);
        await WriteSettingsAsync(storage, notStartedCourseId, plannedSettings);
        await WriteSettingsAsync(storage, notCreatedYetCourseId, plannedSettings);

        await WriteRecordsAsync(storage, partialCourseId,
        [
            new DailyStudyRecord
            {
                CourseId = partialCourseId,
                Date = targetDate,
                NonLessonMinutesStudied = 10,
                MinutesStudied = 10,
                DailyGoalMinutesAtTheTime = 30,
                Status = DailyStudyStatus.Partial
            }
        ]);

        var records = await service.GetDailyRecordsAsync(
            [partialCourseId, notStartedCourseId, notCreatedYetCourseId],
            targetDate);

        Assert.Equal(3, records.Count);
        Assert.Equal(DailyStudyStatus.Partial, records[partialCourseId].Status);
        Assert.Equal(DailyStudyStatus.NotStarted, records[notStartedCourseId].Status);
        Assert.Equal(DailyStudyStatus.Unplanned, records[notCreatedYetCourseId].Status);
    }

    [Fact]
    public async Task GetDailyRecordsAsync_ReturnsEmptyDictionary_WhenNoCourseIdIsProvided()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var storage = new TestStoragePathsService(_rootDirectory);
        var service = new RoutineService(storage, new TestDbContextFactory(options));

        var records = await service.GetDailyRecordsAsync([], new DateTime(2026, 4, 15));

        Assert.Empty(records);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_GeneratesCreditFromDayAboveGoal()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 45)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var extraDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 31)));

        Assert.Equal(DailyStudyStatus.Completed, extraDay.RawStatus);
        Assert.Equal(45, extraDay.MinutesStudied);
        Assert.Equal(30, extraDay.DailyGoalMinutesAtTheTime);
        Assert.Equal(15, extraDay.ExtraMinutes);
        Assert.Equal(0, extraDay.MissingMinutes);
        Assert.False(extraDay.IsMonthlyCreditApplied);
        Assert.True(extraDay.CountsAsEffectiveGoalMet);
        Assert.Equal(100d, extraDay.EffectiveCompliancePercentage);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_AbonatesMostRecentPendingDayFirst()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 29), 40),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 20),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 20)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var olderPendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 30)));
        var mostRecentPendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 31)));

        Assert.False(olderPendingDay.IsMonthlyCreditApplied);
        Assert.True(mostRecentPendingDay.IsMonthlyCreditApplied);
        Assert.True(mostRecentPendingDay.CountsAsEffectiveGoalMet);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_DoesNotAbonateOlderDayBeforeMoreRecentWhenCreditIsInsufficient()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 29), 40),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 20),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 10)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var olderPendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 30)));
        var mostRecentPendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 31)));

        Assert.False(mostRecentPendingDay.IsMonthlyCreditApplied);
        Assert.False(olderPendingDay.IsMonthlyCreditApplied);
        Assert.False(mostRecentPendingDay.CountsAsEffectiveGoalMet);
        Assert.False(olderPendingDay.CountsAsEffectiveGoalMet);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_OnlyAppliesCreditWhenFullDeficitIsCovered()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 35),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 20)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var pendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 31)));

        Assert.False(pendingDay.IsMonthlyCreditApplied);
        Assert.False(pendingDay.CountsAsEffectiveGoalMet);
        Assert.Equal(pendingDay.RawCompliancePercentage, pendingDay.EffectiveCompliancePercentage);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_DoesNotTransferCreditBetweenMonths()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 2, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 2, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 2, 28), 60),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 0)
        ]);

        var februaryEvaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 2);
        var marchEvaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var februaryCreditDay = Assert.Single(februaryEvaluations.Where(item => item.Date == new DateTime(2026, 2, 28)));
        var marchPendingDay = Assert.Single(marchEvaluations.Where(item => item.Date == new DateTime(2026, 3, 31)));

        Assert.Equal(30, februaryCreditDay.ExtraMinutes);
        Assert.False(marchPendingDay.IsMonthlyCreditApplied);
        Assert.False(marchPendingDay.CountsAsEffectiveGoalMet);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_IgnoresUnplannedDays()
    {
        var courseId = Guid.NewGuid();
        var addedAt = new DateTime(2026, 3, 30, 10, 0, 0);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, addedAt));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(addedAt.Date));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 29), 0),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 60),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 0)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var unplannedDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 29)));
        var plannedPendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 31)));

        Assert.False(unplannedDay.IsPlannedDay);
        Assert.False(unplannedDay.IsMonthlyCreditApplied);
        Assert.False(unplannedDay.CountsAsEffectiveGoalMet);
        Assert.True(plannedPendingDay.IsMonthlyCreditApplied);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_PreservesRawStatusOriginal()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 10),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 50)
        ]);

        var rawRecords = await service.GetMonthlyRecordsAsync(courseId, 2026, 3);
        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var rawPendingDay = Assert.Single(rawRecords.Where(item => item.Date == new DateTime(2026, 3, 30)));
        var evaluatedPendingDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 30)));

        Assert.Equal(DailyStudyStatus.Partial, rawPendingDay.Status);
        Assert.Equal(DailyStudyStatus.Partial, evaluatedPendingDay.RawStatus);
        Assert.True(evaluatedPendingDay.IsMonthlyCreditApplied);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_SetsEffectivePercentageTo100ForCreditAppliedDay()
    {
        var courseId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 10),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 50)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var creditedDay = Assert.Single(evaluations.Where(item => item.Date == new DateTime(2026, 3, 30)));

        Assert.True(creditedDay.IsMonthlyCreditApplied);
        Assert.True(creditedDay.RawCompliancePercentage < 100d);
        Assert.Equal(100d, creditedDay.EffectiveCompliancePercentage);
        Assert.True(creditedDay.CountsAsEffectiveGoalMet);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_ConsumesCreditAndLeavesZeroAvailableBalance_WhenSingleAbonoUsesEverything()
    {
        var courseId = Guid.NewGuid();
        var addedAt = new DateTime(2026, 3, 29);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, addedAt));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateSettings(addedAt, 60, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 29), 0, 60),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 0, 60),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 120, 60)
        ]);

        var rawRecords = await service.GetMonthlyRecordsAsync(courseId, 2026, 3);
        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var olderPendingDay = Assert.Single(evaluations, item => item.Date == new DateTime(2026, 3, 29));
        var mostRecentPendingDay = Assert.Single(evaluations, item => item.Date == new DateTime(2026, 3, 30));
        var extraDay = Assert.Single(evaluations, item => item.Date == new DateTime(2026, 3, 31));
        var rawOlderPendingDay = Assert.Single(rawRecords, item => item.Date == new DateTime(2026, 3, 29));
        var rawMostRecentPendingDay = Assert.Single(rawRecords, item => item.Date == new DateTime(2026, 3, 30));

        Assert.Equal(60, extraDay.ExtraMinutes);
        Assert.True(mostRecentPendingDay.IsMonthlyCreditApplied);
        Assert.Equal(60, mostRecentPendingDay.ConsumedMonthlyCreditMinutes);
        Assert.True(mostRecentPendingDay.CountsAsEffectiveGoalMet);
        Assert.Equal(100d, mostRecentPendingDay.EffectiveCompliancePercentage);
        Assert.Equal(0, mostRecentPendingDay.AvailableMonthlyCreditMinutes);
        Assert.False(olderPendingDay.IsMonthlyCreditApplied);
        Assert.Equal(0, olderPendingDay.ConsumedMonthlyCreditMinutes);
        Assert.Equal(0, olderPendingDay.AvailableMonthlyCreditMinutes);
        Assert.False(olderPendingDay.CountsAsEffectiveGoalMet);
        Assert.Equal(DailyStudyStatus.NotStarted, rawOlderPendingDay.Status);
        Assert.Equal(DailyStudyStatus.NotStarted, rawMostRecentPendingDay.Status);
        Assert.Equal(DailyStudyStatus.NotStarted, olderPendingDay.RawStatus);
        Assert.Equal(DailyStudyStatus.NotStarted, mostRecentPendingDay.RawStatus);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_KeepsPositiveAvailableBalance_WhenCreditRemainsAfterAbono()
    {
        var courseId = Guid.NewGuid();
        var addedAt = new DateTime(2026, 3, 30);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, addedAt));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateSettings(addedAt, 60, DayOfWeek.Monday, DayOfWeek.Tuesday));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 0, 60),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 180, 60)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var pendingDay = Assert.Single(evaluations, item => item.Date == new DateTime(2026, 3, 30));
        var extraDay = Assert.Single(evaluations, item => item.Date == new DateTime(2026, 3, 31));

        Assert.True(pendingDay.IsMonthlyCreditApplied);
        Assert.Equal(60, pendingDay.ConsumedMonthlyCreditMinutes);
        Assert.Equal(60, pendingDay.AvailableMonthlyCreditMinutes);
        Assert.Equal(120, extraDay.ExtraMinutes);
        Assert.Equal(60, extraDay.AvailableMonthlyCreditMinutes);
    }

    [Fact]
    public async Task GetMonthlyGoalEvaluationsAsync_AvailableBalance_DoesNotUseCreditFromAnotherMonth()
    {
        var courseId = Guid.NewGuid();
        var addedAt = new DateTime(2026, 2, 28);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, addedAt));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateSettings(addedAt, 60, DayOfWeek.Saturday, DayOfWeek.Tuesday));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 2, 28), 180, 60),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 31), 0, 60)
        ]);

        var februaryEvaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 2);
        var marchEvaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var februaryDay = Assert.Single(februaryEvaluations, item => item.Date == new DateTime(2026, 2, 28));
        var marchPendingDay = Assert.Single(marchEvaluations, item => item.Date == new DateTime(2026, 3, 31));

        Assert.Equal(120, februaryDay.ExtraMinutes);
        Assert.Equal(120, februaryDay.AvailableMonthlyCreditMinutes);
        Assert.False(marchPendingDay.IsMonthlyCreditApplied);
        Assert.Equal(0, marchPendingDay.AvailableMonthlyCreditMinutes);
    }

    [Fact]
    public async Task GetCurrentStreakAsync_RemainsBasedOnRawStudyEvenWhenMonthlyCreditWouldAbonateToday()
    {
        var courseId = Guid.NewGuid();
        var referenceDate = new DateTime(2026, 3, 31);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = await CreateOptionsAsync(connection);
        await SeedCourseAsync(options, CreateCourseRecord(courseId, new DateTime(2026, 3, 1)));

        var service = CreateService(options, out var storage);
        await WriteSettingsAsync(storage, courseId, CreateAllDaysSettings(new DateTime(2026, 3, 1)));
        await WriteRecordsAsync(storage, courseId,
        [
            CreateDailyRecord(courseId, new DateTime(2026, 3, 29), 30),
            CreateDailyRecord(courseId, new DateTime(2026, 3, 30), 60),
            CreateDailyRecord(courseId, referenceDate, 0)
        ]);

        var evaluations = await service.GetMonthlyGoalEvaluationsAsync(courseId, 2026, 3);
        var todayEvaluation = Assert.Single(evaluations.Where(item => item.Date == referenceDate));
        var streak = await service.GetCurrentStreakAsync(courseId, referenceDate);

        Assert.True(todayEvaluation.IsMonthlyCreditApplied);
        Assert.Equal(2, streak);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private RoutineService CreateService(
        DbContextOptions<StudyHubDbContext> options,
        out TestStoragePathsService storage)
    {
        storage = new TestStoragePathsService(_rootDirectory);
        return new RoutineService(storage, new TestDbContextFactory(options));
    }

    private static async Task<DbContextOptions<StudyHubDbContext>> CreateOptionsAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new StudyHubDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return options;
    }

    private static async Task SeedCourseAsync(
        DbContextOptions<StudyHubDbContext> options,
        CourseRecord courseRecord)
    {
        await using var context = new StudyHubDbContext(options);
        context.Courses.Add(courseRecord);
        await context.SaveChangesAsync();
    }

    private static CourseRecord CreateCourseRecord(Guid courseId, DateTime addedAt)
    {
        return new CourseRecord
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
            AddedAt = addedAt
        };
    }

    private static RoutineSettings CreateAllDaysSettings(DateTime lastUpdatedAt, int dailyGoalMinutes = 30)
    {
        return new RoutineSettings
        {
            DailyGoalMinutes = dailyGoalMinutes,
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
            LastUpdatedAt = lastUpdatedAt
        };
    }

    private static RoutineSettings CreateSettings(DateTime lastUpdatedAt, int dailyGoalMinutes, params DayOfWeek[] selectedDays)
    {
        return new RoutineSettings
        {
            DailyGoalMinutes = dailyGoalMinutes,
            SelectedDaysOfWeek = selectedDays.ToList(),
            LastUpdatedAt = lastUpdatedAt
        };
    }

    private static DailyStudyRecord CreateDailyRecord(Guid courseId, DateTime date, int minutes, int dailyGoalMinutes = 30)
    {
        return new DailyStudyRecord
        {
            CourseId = courseId,
            Date = date,
            NonLessonMinutesStudied = minutes,
            MinutesStudied = minutes,
            DailyGoalMinutesAtTheTime = dailyGoalMinutes
        };
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
