using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Contracts.CourseContentSync;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class CourseContentSyncServiceTests : IDisposable
{
    private const string LessonRelativePath = "Module 01/Topic 01/Lesson 01.mp4";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "studyhub-content-sync-service-tests",
        Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<StudyHubDbContext> _options;
    private readonly TestDbContextFactory _contextFactory;

    public CourseContentSyncServiceTests()
    {
        Directory.CreateDirectory(_testRoot);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(_options);

        using var context = new StudyHubDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task PreviewAsync_UnchangedCourse_IsReadOnlyAndDeterministic()
    {
        var rootPath = CourseRoot("unchanged");
        await CreateCourseFileAsync(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var service = CreateService(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var databaseBefore = await CaptureDatabaseStateAsync();

        var first = await service.PreviewAsync(seed.CourseId);
        var second = await service.PreviewAsync(seed.CourseId);
        var databaseAfter = await CaptureDatabaseStateAsync();

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, first.Status);
        Assert.True(first.Success);
        Assert.False(first.HasChanges);
        Assert.False(first.CanApply);
        Assert.Equal(Path.GetFullPath(rootPath), first.RootPath);
        Assert.NotNull(first.ScannedAtUtc);
        Assert.Equal(1, first.ExistingModuleCount);
        Assert.Equal(1, first.CandidateModuleCount);
        Assert.Equal(1, first.UnchangedModuleCount);
        Assert.Equal(1, first.ExistingTopicCount);
        Assert.Equal(1, first.CandidateTopicCount);
        Assert.Equal(1, first.UnchangedTopicCount);
        Assert.Equal(1, first.ExistingLessonCount);
        Assert.Equal(1, first.CandidateLessonCount);
        Assert.Equal(1, first.UnchangedLessonCount);
        Assert.Equal(seed.ModuleId, Assert.Single(first.Modules).ExistingModuleId);
        Assert.Equal(seed.TopicId, Assert.Single(first.Topics).ExistingTopicId);
        Assert.Equal(seed.LessonId, Assert.Single(first.Lessons).ExistingLessonId);
        Assert.Equal(SemanticPreviewJson(first), SemanticPreviewJson(second));
        Assert.Equal(databaseBefore, databaseAfter);
    }

    [Fact]
    public async Task PreviewAsync_NewAndMissingContent_IsReadOnly()
    {
        var rootPath = CourseRoot("ready-read-only");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 14, 45, 0, DateTimeKind.Utc),
            new DetectedLessonSpec(
                "Module 01",
                "Module 01/Topic 01",
                "Module 01/Topic 01/New Lesson.mp4",
                TimeSpan.FromMinutes(6)));
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));
        var databaseBefore = await CaptureDatabaseStateAsync();

        var preview = await service.PreviewAsync(seed.CourseId);
        var databaseAfter = await CaptureDatabaseStateAsync();

        Assert.Equal(CourseContentSyncPreviewStatus.Ready, preview.Status);
        Assert.True(preview.Success);
        Assert.True(preview.HasChanges);
        Assert.True(preview.CanApply);
        Assert.Equal(1, preview.UnchangedModuleCount);
        Assert.Equal(1, preview.UnchangedTopicCount);
        Assert.Equal(1, preview.NewLessonCount);
        Assert.Equal(1, preview.MissingLessonCount);
        Assert.Equal(databaseBefore, databaseAfter);
    }

    [Fact]
    public async Task PreviewAsync_InvalidMetadataRoot_FallsBackToFolderPath()
    {
        var rootPath = CourseRoot("folder-fallback");
        await CreateCourseFileAsync(rootPath);
        var seed = await SeedCourseAsync(
            rootPath,
            sourceMetadataJson: "{invalid-json",
            folderPath: rootPath);
        var scanner = new RecordingScanner(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var service = CreateService(scanner);

        var result = await service.PreviewAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, result.Status);
        Assert.Equal(Path.GetFullPath(rootPath), result.RootPath);
        Assert.Equal(Path.GetFullPath(rootPath), Assert.Single(scanner.ScannedRootPaths));
    }

    [Fact]
    public async Task PreviewAsync_ValidUnavailableMetadataRoot_DoesNotUseFolderPath()
    {
        var fallbackRoot = CourseRoot("available-folder-column");
        var unavailableMetadataRoot = CourseRoot("unavailable-metadata-root");
        await CreateCourseFileAsync(fallbackRoot);
        var metadataJson = JsonSerializer.Serialize(
            new CourseSourceMetadata { RootPath = unavailableMetadataRoot },
            JsonOptions);
        var seed = await SeedCourseAsync(
            fallbackRoot,
            sourceMetadataJson: metadataJson,
            folderPath: fallbackRoot);
        var scanner = new RecordingScanner(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var service = CreateService(scanner);

        var result = await service.PreviewAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncPreviewStatus.SourceUnavailable, result.Status);
        Assert.False(result.Success);
        Assert.False(result.CanApply);
        Assert.Equal(Path.GetFullPath(unavailableMetadataRoot), result.RootPath);
        Assert.Empty(scanner.ScannedRootPaths);
    }

    [Fact]
    public async Task PreviewAsync_EmptyFolder_ReturnsNoVideosFoundWithoutChangingDatabase()
    {
        var rootPath = CourseRoot("empty");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var service = CreateService(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var databaseBefore = await CaptureDatabaseStateAsync();

        var result = await service.PreviewAsync(seed.CourseId);
        var databaseAfter = await CaptureDatabaseStateAsync();

        Assert.Equal(CourseContentSyncPreviewStatus.NoVideosFound, result.Status);
        Assert.False(result.Success);
        Assert.False(result.HasChanges);
        Assert.False(result.CanApply);
        Assert.Empty(result.Modules);
        Assert.Empty(result.Topics);
        Assert.Empty(result.Lessons);
        Assert.Equal(databaseBefore, databaseAfter);
    }

    [Fact]
    public async Task PreviewAsync_UnknownCourse_ReturnsCourseNotFoundWithoutScanning()
    {
        var scanner = new RecordingScanner(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var service = CreateService(scanner);
        var courseId = Guid.NewGuid();

        var result = await service.PreviewAsync(courseId);

        Assert.Equal(courseId, result.CourseId);
        Assert.Equal(CourseContentSyncPreviewStatus.CourseNotFound, result.Status);
        Assert.False(result.Success);
        Assert.False(result.CanApply);
        Assert.Empty(scanner.ScannedRootPaths);
    }

    [Fact]
    public async Task PreviewAsync_UnsupportedSource_ReturnsControlledErrorWithoutScanning()
    {
        var rootPath = CourseRoot("unsupported");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath, sourceType: (CourseSourceType)999);
        var scanner = new RecordingScanner(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var service = CreateService(scanner);

        var result = await service.PreviewAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncPreviewStatus.UnsupportedCourseSource, result.Status);
        Assert.False(result.Success);
        Assert.Empty(scanner.ScannedRootPaths);
    }

    [Theory]
    [InlineData("access", CourseContentSyncPreviewStatus.AccessDenied)]
    [InlineData("missing", CourseContentSyncPreviewStatus.SourceUnavailable)]
    [InlineData("io", CourseContentSyncPreviewStatus.ScanFailed)]
    [InlineData("unexpected", CourseContentSyncPreviewStatus.Unexpected)]
    public async Task PreviewAsync_ScannerFailure_ReturnsControlledStatus(
        string failureKind,
        CourseContentSyncPreviewStatus expectedStatus)
    {
        var rootPath = CourseRoot($"scanner-{failureKind}");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var scanner = new DelegateScanner((_, _) =>
            Task.FromException<DetectedCourseStructure>(CreateScannerException(failureKind)));
        var service = CreateService(scanner);

        var result = await service.PreviewAsync(seed.CourseId);

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(result.Success);
        Assert.False(result.CanApply);
    }

    [Fact]
    public async Task PreviewAsync_ScannerCancellation_PropagatesOperationCanceledException()
    {
        var rootPath = CourseRoot("scanner-cancelled");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var scanner = new DelegateScanner((_, cancellationToken) =>
            throw new OperationCanceledException(cancellationToken));
        var service = CreateService(scanner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.PreviewAsync(seed.CourseId));
    }

    [Fact]
    public async Task PreviewAsync_PreCanceledToken_StopsBeforeScanning()
    {
        var rootPath = CourseRoot("pre-cancelled");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var scanner = new RecordingScanner(new LocalCourseScanner(new FakeVideoMetadataReader()));
        var service = CreateService(scanner);
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.PreviewAsync(seed.CourseId, cancellationSource.Token));

        Assert.Empty(scanner.ScannedRootPaths);
    }

    [Fact]
    public async Task ApplyAsync_ScannerCancellationPropagatesWithoutChangingDatabase()
    {
        var rootPath = CourseRoot("apply-scanner-cancelled");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var service = CreateService(new DelegateScanner((_, cancellationToken) =>
            throw new OperationCanceledException(cancellationToken)));
        var databaseBefore = await CaptureDatabaseStateAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ApplyAsync(seed.CourseId));

        Assert.Equal(databaseBefore, await CaptureDatabaseStateAsync());
    }

    [Fact]
    public async Task ApplyAsync_NoChanges_PreservesStateAndRefreshesMetadataAndSnapshot()
    {
        var rootPath = CourseRoot("apply-no-changes");
        Directory.CreateDirectory(rootPath);
        var importedAt = new DateTime(2026, 5, 20, 8, 30, 0, DateTimeKind.Utc);
        var scannedAt = new DateTime(2026, 7, 14, 15, 0, 0, DateTimeKind.Utc);
        var metadata = new JsonObject
        {
            ["rootPath"] = Path.GetFullPath(rootPath),
            ["importedAt"] = importedAt,
            ["introSkipEnabled"] = true,
            ["introSkipSeconds"] = 19,
            ["provider"] = "local-folder",
            ["scanVersion"] = 7,
            ["customFutureField"] = "preserve-me"
        }.ToJsonString(JsonOptions);
        var seed = await SeedCourseAsync(rootPath, sourceMetadataJson: metadata);
        var detected = CreateDetectedStructure(
            rootPath,
            scannedAt,
            new DetectedLessonSpec(
                "Module 01",
                "Module 01/Topic 01",
                LessonRelativePath,
                TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(17)));
        detected.PresentationRootRelativePath = "Module 01";
        var scannerIds = GetDetectedIds(detected);
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var result = await service.ApplyAsync(seed.CourseId);
        var course = await LoadCourseGraphAsync(seed.CourseId);
        var snapshot = await LoadSnapshotAsync(seed.CourseId);
        var manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(
            snapshot.StructureJson,
            JsonOptions)!;
        var manifestLesson = Assert.Single(
            manifest.Modules.SelectMany(item => item.Topics).SelectMany(item => item.Lessons));
        var rootManifestLesson = Assert.Single(EnumerateRootLessons(manifest.RootNode));
        var firstStructureJson = snapshot.StructureJson;

        var secondResult = await service.ApplyAsync(seed.CourseId);
        var secondSnapshot = await LoadSnapshotAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncApplyStatus.NoChanges, result.Status);
        Assert.True(result.Success);
        Assert.False(result.AppliedChanges);
        Assert.Equal(seed.CurrentIds, GetPersistedIds(course));
        Assert.Equal(seed.LessonId, course.CurrentLessonId);
        Assert.Equal("Edited sync course", course.Title);
        var module = Assert.Single(course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);
        Assert.Equal("Edited module", module.Title);
        Assert.Equal("Edited topic", topic.Title);
        Assert.Equal(new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc), topic.CompletedAtUtc);
        Assert.Equal("Edited lesson", lesson.Title);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(42.5, lesson.WatchedPercentage);
        Assert.Equal(123, lesson.LastPlaybackPositionSeconds);
        Assert.Equal(5, lesson.DurationMinutes);
        Assert.Equal(5, course.TotalDurationMinutes);
        Assert.True(module.IsAvailable);
        Assert.True(topic.IsAvailable);
        Assert.True(lesson.IsAvailable);

        using var metadataDocument = JsonDocument.Parse(course.SourceMetadataJson);
        var metadataRoot = metadataDocument.RootElement;
        Assert.Equal(scannedAt, metadataRoot.GetProperty("lastScannedAtUtc").GetDateTime());
        Assert.Equal(importedAt, metadataRoot.GetProperty("importedAt").GetDateTime());
        Assert.True(metadataRoot.GetProperty("introSkipEnabled").GetBoolean());
        Assert.Equal(19, metadataRoot.GetProperty("introSkipSeconds").GetInt32());
        Assert.Equal("local-folder", metadataRoot.GetProperty("provider").GetString());
        Assert.Equal(7, metadataRoot.GetProperty("scanVersion").GetInt32());
        Assert.Equal("preserve-me", metadataRoot.GetProperty("customFutureField").GetString());

        Assert.Equal(new DateTime(2026, 6, 1, 10, 1, 0, DateTimeKind.Utc), snapshot.ImportedAt);
        Assert.Equal(Path.GetFullPath(rootPath), snapshot.RootFolderPath);
        Assert.Equal(seed.CourseId, manifest.CourseId);
        Assert.Equal(seed.CurrentIds, GetManifestIds(manifest));
        Assert.Empty(scannerIds.Intersect(seed.CurrentIds));
        Assert.Equal("Module 01", manifest.PresentationRootRelativePath);
        Assert.Equal(100, manifestLesson.FileSizeBytes);
        Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(17), manifestLesson.Duration);
        Assert.Equal(manifestLesson.FileSizeBytes, rootManifestLesson.FileSizeBytes);
        Assert.Equal(manifestLesson.Duration, rootManifestLesson.Duration);
        Assert.Equal(CourseContentSyncApplyStatus.NoChanges, secondResult.Status);
        Assert.Equal(firstStructureJson, secondSnapshot.StructureJson);
    }

    [Fact]
    public async Task ApplyAsync_NewTree_UsesPermanentIdsPreservesCompletedTopicAndSecondApplyDoesNotDuplicate()
    {
        var rootPath = CourseRoot("apply-new-tree");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 10, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(5)),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", "Module 01/Topic 01/Lesson 02.mp4", TimeSpan.FromMinutes(6), LessonOrder: 2),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 02", "Module 01/Topic 02/Lesson 01.mp4", TimeSpan.FromMinutes(7), TopicOrder: 2),
            new DetectedLessonSpec("Module 02", "Module 02/Topic 01", "Module 02/Topic 01/Lesson 01.mp4", TimeSpan.FromMinutes(8), ModuleOrder: 2));
        var scannerIds = GetDetectedIds(detected);
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var first = await service.ApplyAsync(seed.CourseId);
        var afterFirst = await LoadCourseGraphAsync(seed.CourseId);
        var idsAfterFirst = GetPersistedIds(afterFirst);
        var second = await service.ApplyAsync(seed.CourseId);
        var afterSecond = await LoadCourseGraphAsync(seed.CourseId);
        var snapshot = await LoadSnapshotAsync(seed.CourseId);
        var manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(snapshot.StructureJson, JsonOptions)!;

        Assert.Equal(CourseContentSyncApplyStatus.Applied, first.Status);
        Assert.Equal(1, first.CreatedModuleCount);
        Assert.Equal(2, first.CreatedTopicCount);
        Assert.Equal(3, first.CreatedLessonCount);
        Assert.Equal(2, afterFirst.Modules.Count);
        Assert.Equal(3, afterFirst.Modules.Sum(module => module.Topics.Count));
        Assert.Equal(4, afterFirst.Modules.SelectMany(module => module.Topics).Sum(topic => topic.Lessons.Count));
        Assert.Contains(seed.ModuleId, afterFirst.Modules.Select(module => module.Id));
        Assert.Contains(seed.TopicId, afterFirst.Modules.SelectMany(module => module.Topics).Select(topic => topic.Id));
        Assert.Contains(seed.LessonId, afterFirst.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Select(lesson => lesson.Id));
        Assert.Empty(scannerIds.Intersect(idsAfterFirst));

        var completedTopic = afterFirst.Modules
            .SelectMany(module => module.Topics)
            .Single(topic => topic.Id == seed.TopicId);
        Assert.Equal(new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc), completedTopic.CompletedAtUtc);
        var existingLesson = completedTopic.Lessons.Single(lesson => lesson.Id == seed.LessonId);
        Assert.Equal(LessonStatus.InProgress, existingLesson.Status);
        Assert.Equal(42.5, existingLesson.WatchedPercentage);
        Assert.Equal(123, existingLesson.LastPlaybackPositionSeconds);
        var newLesson = completedTopic.Lessons.Single(lesson => lesson.Id != seed.LessonId);
        Assert.Equal(LessonStatus.NotStarted, newLesson.Status);
        Assert.Equal(0, newLesson.WatchedPercentage);
        Assert.Equal(0, newLesson.LastPlaybackPositionSeconds);
        Assert.Equal(seed.LessonId, afterFirst.CurrentLessonId);

        Assert.Equal(CourseContentSyncApplyStatus.NoChanges, second.Status);
        Assert.Equal(idsAfterFirst, GetPersistedIds(afterSecond));
        Assert.Equal(2, afterSecond.Modules.Count);
        Assert.Equal(4, afterSecond.Modules.SelectMany(module => module.Topics).Sum(topic => topic.Lessons.Count));
        Assert.Equal(idsAfterFirst, GetManifestIds(manifest));
    }

    [Fact]
    public async Task ApplyAsync_CourseExpandsFromTwoToFourModulesWithoutReplacingExistingIds()
    {
        var rootPath = CourseRoot("apply-two-to-four-modules");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 15, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(5)),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", "Module 01/Topic 01/Lesson 02.mp4", TimeSpan.FromMinutes(6), LessonOrder: 2),
            new DetectedLessonSpec("Module 02", "Module 02/Topic 02", "Module 02/Topic 02/Lesson 03.mp4", TimeSpan.FromMinutes(7), ModuleOrder: 2),
            new DetectedLessonSpec("Module 02", "Module 02/Topic 02", "Module 02/Topic 02/Lesson 04.mp4", TimeSpan.FromMinutes(8), ModuleOrder: 2, LessonOrder: 2));
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(currentScan)));

        var baselineApply = await service.ApplyAsync(seed.CourseId);
        var baselineCourse = await LoadCourseGraphAsync(seed.CourseId);
        var baselineIds = GetPersistedIds(baselineCourse);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, baselineApply.Status);
        Assert.Equal(2, baselineCourse.Modules.Count);
        Assert.Equal(2, baselineCourse.Modules.Sum(module => module.Topics.Count));
        Assert.Equal(4, baselineCourse.Modules.SelectMany(module => module.Topics).Sum(topic => topic.Lessons.Count));

        currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 16, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(5)),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", "Module 01/Topic 01/Lesson 02.mp4", TimeSpan.FromMinutes(6), LessonOrder: 2),
            new DetectedLessonSpec("Module 02", "Module 02/Topic 02", "Module 02/Topic 02/Lesson 03.mp4", TimeSpan.FromMinutes(7), ModuleOrder: 2),
            new DetectedLessonSpec("Module 02", "Module 02/Topic 02", "Module 02/Topic 02/Lesson 04.mp4", TimeSpan.FromMinutes(8), ModuleOrder: 2, LessonOrder: 2),
            new DetectedLessonSpec("Module 03", "Module 03/Topic 03", "Module 03/Topic 03/Lesson 05.mp4", TimeSpan.FromMinutes(9), ModuleOrder: 3),
            new DetectedLessonSpec("Module 04", "Module 04/Topic 04", "Module 04/Topic 04/Lesson 06.mp4", TimeSpan.FromMinutes(10), ModuleOrder: 4));
        var scannerIds = GetDetectedIds(currentScan);
        var databaseBeforePreview = await CaptureDatabaseStateAsync();

        var preview = await service.PreviewAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncPreviewStatus.Ready, preview.Status);
        Assert.Equal(2, preview.UnchangedModuleCount);
        Assert.Equal(2, preview.NewModuleCount);
        Assert.Equal(2, preview.NewTopicCount);
        Assert.Equal(2, preview.NewLessonCount);
        Assert.Equal(databaseBeforePreview, await CaptureDatabaseStateAsync());

        var apply = await service.ApplyAsync(seed.CourseId);
        var expandedCourse = await LoadCourseGraphAsync(seed.CourseId);
        var expandedIds = GetPersistedIds(expandedCourse);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, apply.Status);
        Assert.Equal(2, apply.CreatedModuleCount);
        Assert.Equal(2, apply.CreatedTopicCount);
        Assert.Equal(2, apply.CreatedLessonCount);
        Assert.Equal(4, expandedCourse.Modules.Count);
        Assert.True(baselineIds.All(expandedIds.Contains));
        Assert.Empty(scannerIds.Intersect(expandedIds));

        var secondPreview = await service.PreviewAsync(seed.CourseId);
        var secondApply = await service.ApplyAsync(seed.CourseId);
        var finalCourse = await LoadCourseGraphAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, secondPreview.Status);
        Assert.Equal(CourseContentSyncApplyStatus.NoChanges, secondApply.Status);
        Assert.Equal(expandedIds, GetPersistedIds(finalCourse));
        Assert.Equal(4, finalCourse.Modules.Count);
    }

    [Fact]
    public async Task ApplyAsync_MissingLessonThenReappearing_PreservesIdentityProgressAndCurrentLesson()
    {
        var rootPath = CourseRoot("apply-reappearing-lesson");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var missingScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 20, 0, DateTimeKind.Utc),
            new DetectedLessonSpec(
                "Module 01",
                "Module 01/Topic 01",
                "Module 01/Topic 01/Replacement.mp4",
                TimeSpan.FromMinutes(4)));
        var currentScan = missingScan;
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(currentScan)));

        var missingResult = await service.ApplyAsync(seed.CourseId);
        var missingCourse = await LoadCourseGraphAsync(seed.CourseId);
        var missingLesson = missingCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single(lesson => lesson.Id == seed.LessonId);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, missingResult.Status);
        Assert.Equal(1, missingResult.CreatedLessonCount);
        Assert.Equal(1, missingResult.MarkedMissingLessonCount);
        Assert.False(missingLesson.IsAvailable);
        Assert.Equal(LessonStatus.InProgress, missingLesson.Status);
        Assert.Equal(42.5, missingLesson.WatchedPercentage);
        Assert.Equal(123, missingLesson.LastPlaybackPositionSeconds);
        Assert.Equal(seed.LessonId, missingCourse.CurrentLessonId);

        currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 21, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(9)),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", "Module 01/Topic 01/Replacement.mp4", TimeSpan.FromMinutes(4), LessonOrder: 2));

        var restoredResult = await service.ApplyAsync(seed.CourseId);
        var restoredCourse = await LoadCourseGraphAsync(seed.CourseId);
        var restoredLesson = restoredCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single(lesson => lesson.Id == seed.LessonId);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, restoredResult.Status);
        Assert.Equal(1, restoredResult.RestoredAvailableItemCount);
        Assert.True(restoredLesson.IsAvailable);
        Assert.Equal(seed.LessonId, restoredLesson.Id);
        Assert.Equal(LessonStatus.InProgress, restoredLesson.Status);
        Assert.Equal(42.5, restoredLesson.WatchedPercentage);
        Assert.Equal(123, restoredLesson.LastPlaybackPositionSeconds);
        Assert.Equal(seed.LessonId, restoredCourse.CurrentLessonId);
    }

    [Fact]
    public async Task ApplyAsync_MissingTopic_PreservesTopicLessonsAndHistoricalCompletion()
    {
        var rootPath = CourseRoot("apply-missing-topic");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 30, 0, DateTimeKind.Utc),
            new DetectedLessonSpec(
                "Module 01",
                "Module 01/Topic 02",
                "Module 01/Topic 02/Lesson 01.mp4",
                TimeSpan.FromMinutes(4),
                TopicOrder: 2));
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var result = await service.ApplyAsync(seed.CourseId);
        var course = await LoadCourseGraphAsync(seed.CourseId);
        var originalTopic = course.Modules
            .SelectMany(module => module.Topics)
            .Single(topic => topic.Id == seed.TopicId);
        var originalLesson = Assert.Single(originalTopic.Lessons);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, result.Status);
        Assert.Equal(1, result.MarkedMissingTopicCount);
        Assert.Equal(1, result.MarkedMissingLessonCount);
        Assert.False(originalTopic.IsAvailable);
        Assert.False(originalLesson.IsAvailable);
        Assert.Equal(seed.TopicId, originalTopic.Id);
        Assert.Equal(seed.LessonId, originalLesson.Id);
        Assert.Equal(new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc), originalTopic.CompletedAtUtc);
        Assert.Equal(LessonStatus.InProgress, originalLesson.Status);
    }

    [Fact]
    public async Task ApplyAsync_MissingModule_PreservesWholeKnownTreeAndSnapshot()
    {
        var rootPath = CourseRoot("apply-missing-module");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var previousManifest = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 39, 0, DateTimeKind.Utc),
            new DetectedLessonSpec(
                "Module 01",
                "Module 01/Topic 01",
                LessonRelativePath,
                TimeSpan.FromMinutes(17) + TimeSpan.FromSeconds(29)));
        previousManifest.CourseId = seed.CourseId;
        var previousModule = Assert.Single(previousManifest.Modules);
        previousModule.ModuleId = seed.ModuleId;
        var previousTopic = Assert.Single(previousModule.Topics);
        previousTopic.TopicId = seed.TopicId;
        var previousLesson = Assert.Single(previousTopic.Lessons);
        previousLesson.LessonId = seed.LessonId;
        previousLesson.FileSizeBytes = 987_654;
        await using (var context = new StudyHubDbContext(_options))
        {
            var existingSnapshot = await context.CourseImportSnapshots
                .SingleAsync(record => record.CourseId == seed.CourseId);
            existingSnapshot.StructureJson = JsonSerializer.Serialize(previousManifest, JsonOptions);
            await context.SaveChangesAsync();
        }

        var currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 40, 0, DateTimeKind.Utc),
            new DetectedLessonSpec(
                "Module 02",
                "Module 02/Topic 01",
                "Module 02/Topic 01/Lesson 01.mp4",
                TimeSpan.FromMinutes(4),
                ModuleOrder: 2));
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(currentScan)));

        var result = await service.ApplyAsync(seed.CourseId);
        var course = await LoadCourseGraphAsync(seed.CourseId);
        var snapshot = await LoadSnapshotAsync(seed.CourseId);
        var manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(snapshot.StructureJson, JsonOptions)!;
        var oldModule = course.Modules.Single(module => module.Id == seed.ModuleId);
        var oldTopic = Assert.Single(oldModule.Topics);
        var oldLesson = Assert.Single(oldTopic.Lessons);

        Assert.Equal(1, result.MarkedMissingModuleCount);
        Assert.Equal(1, result.MarkedMissingTopicCount);
        Assert.Equal(1, result.MarkedMissingLessonCount);
        Assert.False(oldModule.IsAvailable);
        Assert.False(oldTopic.IsAvailable);
        Assert.False(oldLesson.IsAvailable);
        Assert.Equal(seed.CurrentIds.Concat(GetPersistedIds(course).Except(seed.CurrentIds)).Order(), GetManifestIds(manifest).Order());
        Assert.Contains(manifest.Modules, module => module.ModuleId == seed.ModuleId);
        Assert.Contains(manifest.Modules.SelectMany(module => module.Topics), topic => topic.TopicId == seed.TopicId);
        Assert.Contains(manifest.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons), lesson => lesson.LessonId == seed.LessonId);
        var missingManifestLesson = manifest.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single(lesson => lesson.LessonId == seed.LessonId);
        var missingRootLesson = EnumerateRootLessons(manifest.RootNode)
            .Single(lesson => lesson.LessonId == seed.LessonId);
        Assert.Equal(987_654, missingManifestLesson.FileSizeBytes);
        Assert.Equal(TimeSpan.FromMinutes(17) + TimeSpan.FromSeconds(29), missingManifestLesson.Duration);
        Assert.Equal(missingManifestLesson.FileSizeBytes, missingRootLesson.FileSizeBytes);
        Assert.Equal(missingManifestLesson.Duration, missingRootLesson.Duration);

        var idsAfterMissing = GetPersistedIds(course);
        currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 41, 0, DateTimeKind.Utc),
            new DetectedLessonSpec(
                "Module 01",
                "Module 01/Topic 01",
                LessonRelativePath,
                TimeSpan.FromMinutes(5)),
            new DetectedLessonSpec(
                "Module 02",
                "Module 02/Topic 01",
                "Module 02/Topic 01/Lesson 01.mp4",
                TimeSpan.FromMinutes(4),
                ModuleOrder: 2));

        var restoredResult = await service.ApplyAsync(seed.CourseId);
        var restoredCourse = await LoadCourseGraphAsync(seed.CourseId);
        var restoredModule = restoredCourse.Modules.Single(module => module.Id == seed.ModuleId);
        var restoredTopic = Assert.Single(restoredModule.Topics);
        var restoredLesson = Assert.Single(restoredTopic.Lessons);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, restoredResult.Status);
        Assert.Equal(3, restoredResult.RestoredAvailableItemCount);
        Assert.Equal(0, restoredResult.CreatedModuleCount);
        Assert.Equal(0, restoredResult.CreatedTopicCount);
        Assert.Equal(0, restoredResult.CreatedLessonCount);
        Assert.True(restoredModule.IsAvailable);
        Assert.True(restoredTopic.IsAvailable);
        Assert.True(restoredLesson.IsAvailable);
        Assert.Equal(LessonStatus.InProgress, restoredLesson.Status);
        Assert.Equal(42.5, restoredLesson.WatchedPercentage);
        Assert.Equal(123, restoredLesson.LastPlaybackPositionSeconds);
        Assert.Equal(seed.LessonId, restoredCourse.CurrentLessonId);
        Assert.Equal(idsAfterMissing, GetPersistedIds(restoredCourse));
    }

    [Fact]
    public async Task ApplyAsync_RenamedLesson_KeepsOldProgressAndCreatesDistinctPendingLesson()
    {
        var rootPath = CourseRoot("apply-renamed-lesson");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        const string renamedPath = "Module 01/Topic 01/Renamed Lesson.mp4";
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 15, 50, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", renamedPath, TimeSpan.FromMinutes(4)));
        var scannerLessonId = Assert.Single(detected.Modules).Topics.Single().Lessons.Single().LessonId;
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var result = await service.ApplyAsync(seed.CourseId);
        var course = await LoadCourseGraphAsync(seed.CourseId);
        var lessons = course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).ToList();
        var oldLesson = lessons.Single(lesson => lesson.Id == seed.LessonId);
        var renamedLesson = lessons.Single(lesson => lesson.RelativeFilePath == renamedPath);

        Assert.Equal(1, result.MarkedMissingLessonCount);
        Assert.Equal(1, result.CreatedLessonCount);
        Assert.False(oldLesson.IsAvailable);
        Assert.Equal(LessonStatus.InProgress, oldLesson.Status);
        Assert.Equal(42.5, oldLesson.WatchedPercentage);
        Assert.NotEqual(seed.LessonId, renamedLesson.Id);
        Assert.NotEqual(scannerLessonId, renamedLesson.Id);
        Assert.True(renamedLesson.IsAvailable);
        Assert.Equal(LessonStatus.NotStarted, renamedLesson.Status);
        Assert.Equal(0, renamedLesson.WatchedPercentage);
    }

    [Fact]
    public async Task ApplyAsync_InvalidPlans_AreBlockedWithoutChangingPersistence()
    {
        await AssertBlockedApplyLeavesDatabaseUnchangedAsync(
            "apply-blocked-ambiguous",
            (rootPath, _) => CreateAmbiguousDetectedStructure(rootPath),
            expectedPreviewStatus: CourseContentSyncPreviewStatus.AmbiguousStructure);
        await AssertBlockedApplyLeavesDatabaseUnchangedAsync(
            "apply-blocked-no-videos",
            (rootPath, _) => CreateDetectedStructure(
                rootPath,
                new DateTime(2026, 7, 14, 16, 0, 0, DateTimeKind.Utc)),
            expectedPreviewStatus: CourseContentSyncPreviewStatus.NoVideosFound);

        var unavailableRoot = CourseRoot("apply-blocked-unavailable");
        var unavailableSeed = await SeedCourseAsync(unavailableRoot);
        var unavailableBefore = await CaptureDatabaseStateAsync();
        var unavailableService = CreateService(new DelegateScanner((_, _) =>
            throw new InvalidOperationException("Scanner must not run.")));

        var unavailable = await unavailableService.ApplyAsync(unavailableSeed.CourseId);

        Assert.Equal(CourseContentSyncApplyStatus.Blocked, unavailable.Status);
        Assert.Equal(CourseContentSyncPreviewStatus.SourceUnavailable, unavailable.Preview.Status);
        Assert.Equal(unavailableBefore, await CaptureDatabaseStateAsync());

        var insufficientRoot = CourseRoot("apply-blocked-insufficient");
        Directory.CreateDirectory(insufficientRoot);
        var insufficientSeed = await SeedCourseAsync(insufficientRoot);
        await ClearPersistedStructuralReferencesAsync(insufficientSeed.CourseId);
        var insufficientBefore = await CaptureDatabaseStateAsync();
        var validDetected = CreateDetectedStructure(
            insufficientRoot,
            new DateTime(2026, 7, 14, 16, 5, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(5)));
        var insufficientService = CreateService(new DelegateScanner((_, _) => Task.FromResult(validDetected)));

        var insufficient = await insufficientService.ApplyAsync(insufficientSeed.CourseId);

        Assert.Equal(CourseContentSyncApplyStatus.Blocked, insufficient.Status);
        Assert.Equal(CourseContentSyncPreviewStatus.InsufficientReferenceData, insufficient.Preview.Status);
        Assert.Equal(insufficientBefore, await CaptureDatabaseStateAsync());
    }

    [Fact]
    public async Task ApplyAsync_RootChangedAfterScan_IsBlockedBeforeApplyingPlan()
    {
        var rootPath = CourseRoot("apply-root-race-old");
        var replacementRoot = CourseRoot("apply-root-race-new");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(replacementRoot);
        var seed = await SeedCourseAsync(rootPath);
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 10, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", "Module 01/Topic 01/New.mp4", TimeSpan.FromMinutes(5)));
        var scanner = new DelegateScanner(async (_, cancellationToken) =>
        {
            await ChangeCourseRootAsync(seed.CourseId, replacementRoot, cancellationToken);
            return detected;
        });
        var service = CreateService(scanner);

        var result = await service.ApplyAsync(seed.CourseId);
        var course = await LoadCourseGraphAsync(seed.CourseId);
        var snapshot = await LoadSnapshotAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncApplyStatus.Blocked, result.Status);
        Assert.Equal(Path.GetFullPath(replacementRoot), course.FolderPath);
        using var metadataDocument = JsonDocument.Parse(course.SourceMetadataJson);
        Assert.Equal(Path.GetFullPath(replacementRoot), metadataDocument.RootElement.GetProperty("rootPath").GetString());
        Assert.False(metadataDocument.RootElement.TryGetProperty("lastScannedAtUtc", out _));
        Assert.Equal(seed.CurrentIds, GetPersistedIds(course));
        Assert.Equal("{\"sentinel\":\"must-remain-unchanged\"}", snapshot.StructureJson);
    }

    [Fact]
    public async Task ApplyAsync_SaveFailure_RollsBackNewMissingMetadataAndSnapshot()
    {
        var rootPath = CourseRoot("apply-rollback");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        const string newLessonPath = "Module 01/Topic 01/Fail Insert.mp4";
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 20, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", newLessonPath, TimeSpan.FromMinutes(4)));
        await CreateFailingLessonInsertTriggerAsync(newLessonPath);
        var before = await CaptureDatabaseStateAsync();
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var result = await service.ApplyAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncApplyStatus.Failed, result.Status);
        Assert.False(result.Success);
        Assert.Equal(before, await CaptureDatabaseStateAsync());
    }

    [Fact]
    public async Task ApplyAsync_NewAndMissingContent_RemainsStableAcrossTwoPersistedCourseReads()
    {
        var rootPath = CourseRoot("apply-rehydration");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        const string newLessonPath = "Module 01/Topic 01/New Lesson.mp4";
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 30, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", newLessonPath, TimeSpan.FromMinutes(6)));
        var syncService = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var applied = await syncService.ApplyAsync(seed.CourseId);
        var stateAfterApply = await CaptureDatabaseStateAsync();
        var courseAfterApply = await LoadCourseGraphAsync(seed.CourseId);
        var newLessonId = courseAfterApply.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single(lesson => lesson.RelativeFilePath == newLessonPath)
            .Id;
        var persistedCourseService = new PersistedCourseService(_contextFactory);

        var firstRead = await persistedCourseService.GetCourseByIdAsync(seed.CourseId);
        var stateAfterFirstRead = await CaptureDatabaseStateAsync();
        var secondRead = await persistedCourseService.GetCourseByIdAsync(seed.CourseId);
        var stateAfterSecondRead = await CaptureDatabaseStateAsync();

        Assert.Equal(CourseContentSyncApplyStatus.Applied, applied.Status);
        Assert.NotNull(firstRead);
        Assert.NotNull(secondRead);
        Assert.Equal(stateAfterApply, stateAfterFirstRead);
        Assert.Equal(stateAfterApply, stateAfterSecondRead);
        Assert.Equal(GetDomainIds(firstRead!), GetDomainIds(secondRead!));
        Assert.Contains(seed.LessonId, GetDomainIds(firstRead!));
        Assert.Contains(newLessonId, GetDomainIds(firstRead!));
        var firstOldLesson = firstRead!.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single(lesson => lesson.Id == seed.LessonId);
        var secondOldLesson = secondRead!.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single(lesson => lesson.Id == seed.LessonId);
        Assert.False(firstOldLesson.IsAvailable);
        Assert.False(secondOldLesson.IsAvailable);
        Assert.True(firstRead.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Single(lesson => lesson.Id == newLessonId).IsAvailable);
        Assert.True(secondRead.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Single(lesson => lesson.Id == newLessonId).IsAvailable);
    }

    [Fact]
    public async Task ApplyAsync_NewLesson_LowersRealProgressWithoutChangingRoutineHistory()
    {
        var rootPath = CourseRoot("apply-progress-history");
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        await MarkLessonCompletedAsync(seed.LessonId);
        var historyDate = new DateTime(2026, 7, 10);
        var storage = new TestStoragePathsService(Path.Combine(_testRoot, "routine-history"));
        var routineService = new RoutineService(storage, _contextFactory);
        await routineService.SaveSettingsAsync(
            seed.CourseId,
            CreateAllDaysStudyUnitSettings(),
            historyDate.AddDays(-1));
        await routineService.CreditLessonProgressAsync(seed.CourseId, seed.LessonId, 11, historyDate);
        Assert.True(await routineService.CreditStudyUnitProgressAsync(seed.CourseId, seed.TopicId, historyDate));
        var historyBefore = await routineService.GetDailyRecordAsync(seed.CourseId, historyDate);
        var historyFilePath = GetRoutineRecordsPath(storage, seed.CourseId);
        var historyBytesBefore = await File.ReadAllBytesAsync(historyFilePath);
        var progressService = new PersistedProgressService(_contextFactory, routineService);
        var progressBefore = await progressService.GetProgressByCourseAsync(seed.CourseId);
        const string newLessonPath = "Module 01/Topic 01/New Pending Lesson.mp4";
        var currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 40, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(17)),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", newLessonPath, TimeSpan.FromMinutes(8), LessonOrder: 2));
        var syncService = CreateService(new DelegateScanner((_, _) => Task.FromResult(currentScan)));

        var result = await syncService.ApplyAsync(seed.CourseId);
        var progressAfter = await progressService.GetProgressByCourseAsync(seed.CourseId);
        var historyAfter = await routineService.GetDailyRecordAsync(seed.CourseId, historyDate);
        var historyBytesAfter = await File.ReadAllBytesAsync(historyFilePath);
        var course = await LoadCourseGraphAsync(seed.CourseId);
        var topic = course.Modules.SelectMany(module => module.Topics).Single(item => item.Id == seed.TopicId);
        var oldLesson = topic.Lessons.Single(lesson => lesson.Id == seed.LessonId);
        var newLesson = topic.Lessons.Single(lesson => lesson.RelativeFilePath == newLessonPath);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, result.Status);
        Assert.NotNull(progressBefore);
        Assert.NotNull(progressAfter);
        Assert.Equal(1, progressBefore!.TotalLessons);
        Assert.Equal(1, progressBefore.CompletedLessons);
        Assert.Equal(100, progressBefore.OverallPercentage);
        Assert.Equal(2, progressAfter!.TotalLessons);
        Assert.Equal(1, progressAfter.CompletedLessons);
        Assert.Equal(50, progressAfter.OverallPercentage);
        Assert.Equal(new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc), topic.CompletedAtUtc);
        Assert.Equal(LessonStatus.Completed, oldLesson.Status);
        Assert.Equal(100, oldLesson.WatchedPercentage);
        Assert.Equal(LessonStatus.NotStarted, newLesson.Status);
        Assert.Equal(0, newLesson.WatchedPercentage);
        Assert.Equal(historyBytesBefore, historyBytesAfter);
        Assert.Equal(historyBefore.CompletedStudyUnitIds, historyAfter.CompletedStudyUnitIds);
        Assert.Equal([seed.TopicId], historyAfter.CompletedStudyUnitIds);
        Assert.Equal(historyBefore.MinutesStudied, historyAfter.MinutesStudied);
        Assert.Equal(historyBefore.Status, historyAfter.Status);

        currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 41, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", newLessonPath, TimeSpan.FromMinutes(8)));
        var missingResult = await syncService.ApplyAsync(seed.CourseId);
        var missingCourse = await LoadCourseGraphAsync(seed.CourseId);
        var missingOldLesson = missingCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(item => item.Lessons)
            .Single(lesson => lesson.Id == seed.LessonId);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, missingResult.Status);
        Assert.False(missingOldLesson.IsAvailable);
        Assert.Equal(LessonStatus.Completed, missingOldLesson.Status);
        Assert.Equal(historyBytesBefore, await File.ReadAllBytesAsync(historyFilePath));

        currentScan = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 42, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(17)),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", newLessonPath, TimeSpan.FromMinutes(8), LessonOrder: 2));
        var restoredResult = await syncService.ApplyAsync(seed.CourseId);
        var restoredCourse = await LoadCourseGraphAsync(seed.CourseId);
        var restoredOldLesson = restoredCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(item => item.Lessons)
            .Single(lesson => lesson.Id == seed.LessonId);
        var finalHistory = await routineService.GetDailyRecordAsync(seed.CourseId, historyDate);

        Assert.Equal(CourseContentSyncApplyStatus.Applied, restoredResult.Status);
        Assert.True(restoredOldLesson.IsAvailable);
        Assert.Equal(LessonStatus.Completed, restoredOldLesson.Status);
        Assert.Equal(100, restoredOldLesson.WatchedPercentage);
        Assert.Equal(historyBytesBefore, await File.ReadAllBytesAsync(historyFilePath));
        Assert.Equal(historyBefore.CompletedStudyUnitIds, finalHistory.CompletedStudyUnitIds);
        Assert.Equal(historyBefore.MinutesStudied, finalHistory.MinutesStudied);
        Assert.Equal(historyBefore.Status, finalHistory.Status);
    }

    [Fact]
    public async Task ApplyAsync_MissingSnapshot_CreatesItUsingHistoricalImportTime()
    {
        var metadataRoot = CourseRoot("apply-create-snapshot-metadata");
        Directory.CreateDirectory(metadataRoot);
        var importedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
        var metadataSeed = await SeedCourseAsync(
            metadataRoot,
            sourceMetadataJson: new JsonObject
            {
                ["rootPath"] = Path.GetFullPath(metadataRoot),
                ["importedAt"] = importedAt
            }.ToJsonString(JsonOptions));
        await DeleteSnapshotAsync(metadataSeed.CourseId);
        var metadataDetected = CreateDetectedStructure(
            metadataRoot,
            new DateTime(2026, 7, 14, 16, 50, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(5)));
        var metadataService = CreateService(new DelegateScanner((_, _) => Task.FromResult(metadataDetected)));

        var metadataResult = await metadataService.ApplyAsync(metadataSeed.CourseId);
        var metadataSnapshot = await LoadSnapshotAsync(metadataSeed.CourseId);

        Assert.True(metadataResult.Success);
        Assert.Equal(importedAt, metadataSnapshot.ImportedAt);
        Assert.Equal(Path.GetFullPath(metadataRoot), metadataSnapshot.RootFolderPath);

        var addedAtRoot = CourseRoot("apply-create-snapshot-added-at");
        Directory.CreateDirectory(addedAtRoot);
        var addedAtSeed = await SeedCourseAsync(addedAtRoot);
        await DeleteSnapshotAsync(addedAtSeed.CourseId);
        var addedAtDetected = CreateDetectedStructure(
            addedAtRoot,
            new DateTime(2026, 7, 14, 16, 55, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic 01", LessonRelativePath, TimeSpan.FromMinutes(5)));
        var addedAtService = CreateService(new DelegateScanner((_, _) => Task.FromResult(addedAtDetected)));

        var addedAtResult = await addedAtService.ApplyAsync(addedAtSeed.CourseId);
        var addedAtSnapshot = await LoadSnapshotAsync(addedAtSeed.CourseId);

        Assert.True(addedAtResult.Success);
        Assert.Equal(new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), addedAtSnapshot.ImportedAt);
        Assert.Equal(Path.GetFullPath(addedAtRoot), addedAtSnapshot.RootFolderPath);
    }

    public void Dispose()
    {
        _connection.Dispose();

        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private CourseContentSyncService CreateService(ILocalCourseScanner scanner)
        => new(
            _contextFactory,
            scanner,
            NullLogger<CourseContentSyncService>.Instance);

    private string CourseRoot(string name)
        => Path.Combine(_testRoot, name);

    private static async Task CreateCourseFileAsync(string rootPath)
    {
        var lessonPath = Path.Combine(
            rootPath,
            LessonRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(lessonPath)!);
        Directory.CreateDirectory(Path.Combine(rootPath, "empty-presentation-boundary"));
        await File.WriteAllBytesAsync(lessonPath, []);
    }

    private async Task<SeededCourse> SeedCourseAsync(
        string physicalRootPath,
        string? sourceMetadataJson = null,
        string? folderPath = null,
        CourseSourceType sourceType = CourseSourceType.LocalFolder)
    {
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var normalizedPhysicalRoot = Path.GetFullPath(physicalRootPath);
        var lessonAbsolutePath = Path.GetFullPath(Path.Combine(
            normalizedPhysicalRoot,
            LessonRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var metadataJson = sourceMetadataJson ?? JsonSerializer.Serialize(
            new CourseSourceMetadata { RootPath = normalizedPhysicalRoot },
            JsonOptions);
        var course = new CourseRecord
        {
            Id = courseId,
            RawTitle = "Raw sync course",
            RawDescription = "Raw sync description",
            Title = "Edited sync course",
            Description = "Edited sync description",
            Category = "Local course",
            ThumbnailUrl = "thumb.png",
            FolderPath = folderPath is null ? normalizedPhysicalRoot : Path.GetFullPath(folderPath),
            SourceType = sourceType,
            LifecycleStatus = CourseLifecycleStatus.Active,
            SourceMetadataJson = metadataJson,
            TotalDurationMinutes = 17,
            AddedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            LastAccessedAt = new DateTime(2026, 6, 2, 11, 0, 0, DateTimeKind.Utc),
            CurrentLessonId = lessonId,
            Modules =
            [
                new ModuleRecord
                {
                    Id = moduleId,
                    CourseId = courseId,
                    Order = 1,
                    RawTitle = "Module 01",
                    RawDescription = "Raw module description",
                    Title = "Edited module",
                    Description = "Edited module description",
                    SourceRelativePath = "Module 01",
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = topicId,
                            ModuleId = moduleId,
                            Order = 1,
                            RawTitle = "Topic 01",
                            RawDescription = "Raw topic description",
                            Title = "Edited topic",
                            Description = "Edited topic description",
                            SourceRelativePath = "Module 01/Topic 01",
                            CompletedAtUtc = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc),
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = lessonId,
                                    TopicId = topicId,
                                    Order = 1,
                                    RawTitle = "Lesson 01",
                                    RawDescription = "Raw lesson description",
                                    Title = "Edited lesson",
                                    Description = "Edited lesson description",
                                    FilePath = lessonAbsolutePath,
                                    SourceType = LessonSourceType.LocalFile,
                                    LocalFilePath = lessonAbsolutePath,
                                    RelativeFilePath = LessonRelativePath,
                                    Provider = "LocalFileSystem",
                                    DurationMinutes = 17,
                                    Status = LessonStatus.InProgress,
                                    WatchedPercentage = 42.5,
                                    LastPlaybackPositionSeconds = 123
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var snapshot = new CourseImportSnapshotRecord
        {
            CourseId = courseId,
            SourceKind = "LocalFolder",
            RootFolderPath = normalizedPhysicalRoot,
            StructureJson = "{\"sentinel\":\"must-remain-unchanged\"}",
            ImportedAt = new DateTime(2026, 6, 1, 10, 1, 0, DateTimeKind.Utc)
        };

        await using var context = new StudyHubDbContext(_options);
        context.Courses.Add(course);
        context.CourseImportSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        return new SeededCourse(courseId, moduleId, topicId, lessonId);
    }

    private async Task<string> CaptureDatabaseStateAsync()
    {
        await using var context = new StudyHubDbContext(_options);
        var courses = await context.Courses
            .AsNoTracking()
            .OrderBy(record => record.Id)
            .Select(record => new
            {
                record.Id,
                record.RawTitle,
                record.RawDescription,
                record.Title,
                record.Description,
                record.Category,
                record.ThumbnailUrl,
                record.FolderPath,
                record.SourceType,
                record.LifecycleStatus,
                record.SourceMetadataJson,
                record.TotalDurationMinutes,
                record.AddedAt,
                record.LastAccessedAt,
                record.CurrentLessonId
            })
            .ToListAsync();
        var modules = await context.Modules
            .AsNoTracking()
            .OrderBy(record => record.Id)
            .Select(record => new
            {
                record.Id,
                record.CourseId,
                record.Order,
                record.RawTitle,
                record.RawDescription,
                record.Title,
                record.Description,
                record.SourceRelativePath,
                record.IsAvailable
            })
            .ToListAsync();
        var topics = await context.Topics
            .AsNoTracking()
            .OrderBy(record => record.Id)
            .Select(record => new
            {
                record.Id,
                record.ModuleId,
                record.Order,
                record.RawTitle,
                record.RawDescription,
                record.Title,
                record.Description,
                record.SourceRelativePath,
                record.CompletedAtUtc,
                record.IsAvailable
            })
            .ToListAsync();
        var lessons = await context.Lessons
            .AsNoTracking()
            .OrderBy(record => record.Id)
            .Select(record => new
            {
                record.Id,
                record.TopicId,
                record.Order,
                record.RawTitle,
                record.RawDescription,
                record.Title,
                record.Description,
                record.FilePath,
                record.SourceType,
                record.LocalFilePath,
                record.RelativeFilePath,
                record.Provider,
                record.DurationMinutes,
                record.Status,
                record.WatchedPercentage,
                record.LastPlaybackPositionSeconds,
                record.IsAvailable
            })
            .ToListAsync();
        var snapshots = await context.CourseImportSnapshots
            .AsNoTracking()
            .OrderBy(record => record.CourseId)
            .Select(record => new
            {
                record.CourseId,
                record.SourceKind,
                record.RootFolderPath,
                record.StructureJson,
                record.ImportedAt
            })
            .ToListAsync();

        return JsonSerializer.Serialize(new
        {
            Courses = courses,
            Modules = modules,
            Topics = topics,
            Lessons = lessons,
            Snapshots = snapshots
        }, JsonOptions);
    }

    private async Task<CourseRecord> LoadCourseGraphAsync(Guid courseId)
    {
        await using var context = new StudyHubDbContext(_options);
        return await context.Courses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync(course => course.Id == courseId);
    }

    private async Task<CourseImportSnapshotRecord> LoadSnapshotAsync(Guid courseId)
    {
        await using var context = new StudyHubDbContext(_options);
        return await context.CourseImportSnapshots
            .AsNoTracking()
            .SingleAsync(snapshot => snapshot.CourseId == courseId);
    }

    private async Task AssertBlockedApplyLeavesDatabaseUnchangedAsync(
        string rootName,
        Func<string, SeededCourse, DetectedCourseStructure> createDetected,
        CourseContentSyncPreviewStatus expectedPreviewStatus)
    {
        var rootPath = CourseRoot(rootName);
        Directory.CreateDirectory(rootPath);
        var seed = await SeedCourseAsync(rootPath);
        var detected = createDetected(rootPath, seed);
        var before = await CaptureDatabaseStateAsync();
        var service = CreateService(new DelegateScanner((_, _) => Task.FromResult(detected)));

        var result = await service.ApplyAsync(seed.CourseId);

        Assert.Equal(CourseContentSyncApplyStatus.Blocked, result.Status);
        Assert.False(result.Success);
        Assert.Equal(expectedPreviewStatus, result.Preview.Status);
        Assert.Equal(before, await CaptureDatabaseStateAsync());
    }

    private async Task ClearPersistedStructuralReferencesAsync(Guid courseId)
    {
        await using var context = new StudyHubDbContext(_options);
        var course = await context.Courses
            .Include(record => record.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync(record => record.Id == courseId);
        foreach (var module in course.Modules)
        {
            module.SourceRelativePath = string.Empty;
            foreach (var topic in module.Topics)
            {
                topic.SourceRelativePath = string.Empty;
                foreach (var lesson in topic.Lessons)
                {
                    lesson.RelativeFilePath = string.Empty;
                    lesson.LocalFilePath = string.Empty;
                    lesson.FilePath = string.Empty;
                }
            }
        }

        await context.SaveChangesAsync();
    }

    private async Task MarkLessonCompletedAsync(Guid lessonId)
    {
        await using var context = new StudyHubDbContext(_options);
        var lesson = await context.Lessons.SingleAsync(record => record.Id == lessonId);
        lesson.Status = LessonStatus.Completed;
        lesson.WatchedPercentage = 100;
        lesson.LastPlaybackPositionSeconds = Math.Max(
            lesson.LastPlaybackPositionSeconds,
            (int)TimeSpan.FromMinutes(lesson.DurationMinutes).TotalSeconds);
        await context.SaveChangesAsync();
    }

    private async Task DeleteSnapshotAsync(Guid courseId)
    {
        await using var context = new StudyHubDbContext(_options);
        var snapshot = await context.CourseImportSnapshots.SingleAsync(record => record.CourseId == courseId);
        context.CourseImportSnapshots.Remove(snapshot);
        await context.SaveChangesAsync();
    }

    private async Task ChangeCourseRootAsync(
        Guid courseId,
        string replacementRoot,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(replacementRoot);
        await using var context = new StudyHubDbContext(_options);
        var course = await context.Courses.SingleAsync(
            record => record.Id == courseId,
            cancellationToken);
        course.FolderPath = normalizedRoot;
        course.SourceMetadataJson = new JsonObject
        {
            ["rootPath"] = normalizedRoot
        }.ToJsonString(JsonOptions);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateFailingLessonInsertTriggerAsync(string relativeFilePath)
    {
        var escapedPath = relativeFilePath.Replace("'", "''", StringComparison.Ordinal);
        var sql = $"""
            CREATE TRIGGER fail_course_content_sync_lesson_insert
            BEFORE INSERT ON lessons
            WHEN NEW.relative_file_path = '{escapedPath}'
            BEGIN
                SELECT RAISE(ABORT, 'forced course content sync failure');
            END;
            """;
        await using var context = new StudyHubDbContext(_options);
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static DetectedCourseStructure CreateDetectedStructure(
        string rootPath,
        DateTime scannedAt,
        params DetectedLessonSpec[] lessons)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var modules = lessons
            .GroupBy(lesson => lesson.ModuleSourceRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(moduleGroup => new DetectedModuleStructure
            {
                ModuleId = Guid.NewGuid(),
                Order = moduleGroup.Min(lesson => lesson.ModuleOrder),
                RawName = ResolveDetectedPathName(moduleGroup.Key, "Course"),
                RelativePath = moduleGroup.Key,
                Topics = moduleGroup
                    .GroupBy(lesson => lesson.TopicSourceRelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(topicGroup =>
                    {
                        if (!LocalCourseStructurePathHelper.TryMakeRelativeToParent(
                                moduleGroup.Key,
                                topicGroup.Key,
                                out var topicRelativePath))
                        {
                            throw new InvalidOperationException("The test topic path is outside its module.");
                        }

                        return new DetectedTopicStructure
                        {
                            TopicId = Guid.NewGuid(),
                            Order = topicGroup.Min(lesson => lesson.TopicOrder),
                            RawName = topicRelativePath == "."
                                ? ResolveDetectedPathName(moduleGroup.Key, "Course")
                                : ResolveDetectedPathName(topicRelativePath, "Topic"),
                            RelativePath = topicRelativePath,
                            Lessons = topicGroup
                                .OrderBy(lesson => lesson.LessonOrder)
                                .ThenBy(lesson => lesson.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
                                .Select(lesson => new DetectedLessonFile
                                {
                                    LessonId = Guid.NewGuid(),
                                    Order = lesson.LessonOrder,
                                    RawName = Path.GetFileNameWithoutExtension(lesson.RelativeFilePath),
                                    FileName = Path.GetFileName(lesson.RelativeFilePath),
                                    RelativePath = lesson.RelativeFilePath,
                                    AbsolutePath = Path.GetFullPath(Path.Combine(
                                        normalizedRoot,
                                        lesson.RelativeFilePath.Replace('/', Path.DirectorySeparatorChar))),
                                    Extension = Path.GetExtension(lesson.RelativeFilePath),
                                    FileSizeBytes = 100,
                                    Duration = lesson.Duration
                                })
                                .ToList()
                        };
                    })
                    .OrderBy(topic => topic.Order)
                    .ThenBy(topic => topic.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(module => module.Order)
            .ThenBy(module => module.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DetectedCourseStructure
        {
            CourseId = Guid.NewGuid(),
            RootFolderName = ResolveDetectedPathName(normalizedRoot, "Course"),
            RootFolderPath = normalizedRoot,
            PresentationRootRelativePath = ".",
            ScannedAt = scannedAt,
            RootNode = new DetectedFolderNode
            {
                Name = ResolveDetectedPathName(normalizedRoot, "Course"),
                RelativePath = "."
            },
            Modules = modules
        };
    }

    private static DetectedCourseStructure CreateAmbiguousDetectedStructure(string rootPath)
    {
        var detected = CreateDetectedStructure(
            rootPath,
            new DateTime(2026, 7, 14, 16, 0, 0, DateTimeKind.Utc),
            new DetectedLessonSpec("Module 01", "Module 01/Topic A", "Module 01/Topic A/A.mp4", TimeSpan.FromMinutes(1)));
        var duplicateModule = new DetectedModuleStructure
        {
            ModuleId = Guid.NewGuid(),
            Order = 2,
            RawName = "Module duplicate",
            RelativePath = "Module 01",
            Topics =
            [
                new DetectedTopicStructure
                {
                    TopicId = Guid.NewGuid(),
                    Order = 1,
                    RawName = "Topic B",
                    RelativePath = "Topic B",
                    Lessons =
                    [
                        new DetectedLessonFile
                        {
                            LessonId = Guid.NewGuid(),
                            Order = 1,
                            RawName = "B",
                            FileName = "B.mp4",
                            RelativePath = "Module 01/Topic B/B.mp4",
                            AbsolutePath = Path.Combine(rootPath, "Module 01", "Topic B", "B.mp4"),
                            Extension = ".mp4",
                            Duration = TimeSpan.FromMinutes(1)
                        }
                    ]
                }
            ]
        };
        detected.Modules.Add(duplicateModule);
        return detected;
    }

    private static string ResolveDetectedPathName(string path, string fallback)
    {
        var name = Path.GetFileName(path.TrimEnd('/', Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) || name == "." ? fallback : name;
    }

    private static Guid[] GetDetectedIds(DetectedCourseStructure detected)
        => detected.Modules
            .Select(module => module.ModuleId)
            .Concat(detected.Modules.SelectMany(module => module.Topics).Select(topic => topic.TopicId))
            .Concat(detected.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Select(lesson => lesson.LessonId))
            .Order()
            .ToArray();

    private static Guid[] GetPersistedIds(CourseRecord course)
        => course.Modules
            .Select(module => module.Id)
            .Concat(course.Modules.SelectMany(module => module.Topics).Select(topic => topic.Id))
            .Concat(course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Select(lesson => lesson.Id))
            .Order()
            .ToArray();

    private static Guid[] GetManifestIds(DetectedCourseStructure manifest)
        => manifest.Modules
            .Select(module => module.ModuleId)
            .Concat(manifest.Modules.SelectMany(module => module.Topics).Select(topic => topic.TopicId))
            .Concat(manifest.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Select(lesson => lesson.LessonId))
            .Order()
            .ToArray();

    private static IEnumerable<DetectedLessonFile> EnumerateRootLessons(DetectedFolderNode node)
    {
        foreach (var lesson in node.DirectLessons)
        {
            yield return lesson;
        }

        foreach (var child in node.Children)
        {
            foreach (var lesson in EnumerateRootLessons(child))
            {
                yield return lesson;
            }
        }
    }

    private static Guid[] GetDomainIds(Course course)
        => course.Modules
            .Select(module => module.Id)
            .Concat(course.Modules.SelectMany(module => module.Topics).Select(topic => topic.Id))
            .Concat(course.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Select(lesson => lesson.Id))
            .Order()
            .ToArray();

    private static RoutineSettings CreateAllDaysStudyUnitSettings()
        => new()
        {
            GoalMode = DailyGoalMode.StudyUnits,
            DailyGoalMinutes = 30,
            DailyGoalStudyUnits = 1,
            SelectedDaysOfWeek = Enum.GetValues<DayOfWeek>().ToList()
        };

    private static string GetRoutineRecordsPath(TestStoragePathsService storage, Guid courseId)
        => Path.Combine(storage.RoutineDirectory, courseId.ToString(), "daily_records.json");

    private static string SemanticPreviewJson(CourseContentSyncPreviewResult result)
    {
        var root = JsonSerializer.SerializeToNode(result, JsonOptions)!.AsObject();
        root.Remove("scannedAtUtc");
        return root.ToJsonString(JsonOptions);
    }

    private static Exception CreateScannerException(string failureKind)
        => failureKind switch
        {
            "access" => new UnauthorizedAccessException("denied"),
            "missing" => new DirectoryNotFoundException("missing"),
            "io" => new IOException("I/O failure"),
            _ => new InvalidOperationException("unexpected failure")
        };

    private sealed class FakeVideoMetadataReader : IVideoMetadataReader
    {
        public Task<TimeSpan?> TryReadDurationAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(5));
    }

    private sealed class RecordingScanner(ILocalCourseScanner inner) : ILocalCourseScanner
    {
        private readonly ILocalCourseScanner _inner = inner;

        public List<string> ScannedRootPaths { get; } = [];

        public Task<DetectedCourseStructure> ScanAsync(
            string rootFolderPath,
            CancellationToken cancellationToken = default)
        {
            ScannedRootPaths.Add(rootFolderPath);
            return _inner.ScanAsync(rootFolderPath, cancellationToken);
        }
    }

    private sealed class DelegateScanner(
        Func<string, CancellationToken, Task<DetectedCourseStructure>> scan) : ILocalCourseScanner
    {
        private readonly Func<string, CancellationToken, Task<DetectedCourseStructure>> _scan = scan;

        public Task<DetectedCourseStructure> ScanAsync(
            string rootFolderPath,
            CancellationToken cancellationToken = default)
            => _scan(rootFolderPath, cancellationToken);
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudyHubDbContext> options)
        : IDbContextFactory<StudyHubDbContext>
    {
        private readonly DbContextOptions<StudyHubDbContext> _options = options;

        public StudyHubDbContext CreateDbContext()
            => new(_options);

        public Task<StudyHubDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new StudyHubDbContext(_options));
    }

    private sealed class TestStoragePathsService : IStoragePathsService
    {
        public TestStoragePathsService(string rootDirectory)
        {
            AppDataDirectory = rootDirectory;
            DatabaseDirectory = rootDirectory;
            DatabasePath = Path.Combine(rootDirectory, "studyhub.db");
            BackupsDirectory = Path.Combine(rootDirectory, "backups");
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
        public string CreateUniqueBackupDirectory(string prefix)
            => Path.Combine(BackupsDirectory, prefix);
    }

    private sealed record SeededCourse(
        Guid CourseId,
        Guid ModuleId,
        Guid TopicId,
        Guid LessonId)
    {
        public Guid[] CurrentIds => new[] { ModuleId, TopicId, LessonId }.Order().ToArray();
    }

    private sealed record DetectedLessonSpec(
        string ModuleSourceRelativePath,
        string TopicSourceRelativePath,
        string RelativeFilePath,
        TimeSpan Duration,
        int ModuleOrder = 1,
        int TopicOrder = 1,
        int LessonOrder = 1);
}
