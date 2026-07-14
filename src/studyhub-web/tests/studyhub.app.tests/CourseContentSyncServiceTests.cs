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
                record.SourceRelativePath
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
                record.CompletedAtUtc
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
                record.LastPlaybackPositionSeconds
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

    private sealed record SeededCourse(
        Guid CourseId,
        Guid ModuleId,
        Guid TopicId,
        Guid LessonId);
}
