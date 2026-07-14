using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class LocalFolderCourseBuilderTests : IDisposable
{
    private const string ExpectedRelativePath = "Modulo 01/Topico 01/Aula 01.mp4";
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        "studyhub-local-course-builder-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScanAsync_ThroughInterface_PreservesDeterministicIds()
    {
        var (courseRoot, _) = await CreateCourseFileAsync("scanner-interface-course");
        ILocalCourseScanner scanner = CreateScanner();

        var firstScan = await scanner.ScanAsync(courseRoot);
        var secondScan = await scanner.ScanAsync(courseRoot);

        Assert.Equal(firstScan.CourseId, secondScan.CourseId);
        Assert.Equal(
            firstScan.Modules.Select(module => module.ModuleId).ToArray(),
            secondScan.Modules.Select(module => module.ModuleId).ToArray());
        Assert.Equal(
            firstScan.Modules.SelectMany(module => module.Topics).Select(topic => topic.TopicId).ToArray(),
            secondScan.Modules.SelectMany(module => module.Topics).Select(topic => topic.TopicId).ToArray());
        Assert.Equal(
            firstScan.Modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Select(lesson => lesson.LessonId)
                .ToArray(),
            secondScan.Modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Select(lesson => lesson.LessonId)
                .ToArray());
    }

    [Fact]
    public async Task ScanAsync_WithPreCanceledToken_PropagatesCancellationBeforeScanning()
    {
        ILocalCourseScanner scanner = CreateScanner();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(
            Path.Combine(_rootDirectory, "missing-course"),
            cancellationSource.Token));
    }

    [Fact]
    public async Task ScanAsync_WhenMetadataReaderCancels_PropagatesCancellation()
    {
        var (courseRoot, _) = await CreateCourseFileAsync("scanner-metadata-cancellation-course");
        using var cancellationSource = new CancellationTokenSource();
        ILocalCourseScanner scanner = new LocalCourseScanner(
            new CancelingVideoMetadataReader(cancellationSource));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(
            courseRoot,
            cancellationSource.Token));
    }

    [Fact]
    public async Task BuildAsync_PreservesAbsoluteAndPortableRelativeLessonPaths()
    {
        var (courseRoot, videoPath) = await CreateCourseFileAsync("builder-course");
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(new LocalFolderCourseBuildRequest
        {
            FolderPath = courseRoot
        });

        var detectedLesson = Assert.Single(result.DetectedStructure.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons));
        var module = Assert.Single(result.Course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);
        var expectedAbsolutePath = Path.GetFullPath(videoPath);

        Assert.Equal(expectedAbsolutePath, detectedLesson.AbsolutePath);
        Assert.Equal(ExpectedRelativePath, detectedLesson.RelativePath);
        Assert.Equal(expectedAbsolutePath, lesson.LocalFilePath);
        Assert.True(Path.IsPathFullyQualified(lesson.LocalFilePath));
        Assert.Equal(ExpectedRelativePath, lesson.RelativeFilePath);
        Assert.False(Path.IsPathFullyQualified(lesson.RelativeFilePath));
        Assert.DoesNotContain('\\', lesson.RelativeFilePath);
        Assert.Equal("Modulo 01", module.SourceRelativePath);
        Assert.Equal("Modulo 01/Topico 01", topic.SourceRelativePath);
        Assert.True(module.IsAvailable);
        Assert.True(topic.IsAvailable);
        Assert.True(lesson.IsAvailable);
        Assert.Equal(result.DetectedStructure.ScannedAt, result.Course.SourceMetadata.ImportedAt);
        Assert.Equal(result.DetectedStructure.ScannedAt, result.Course.SourceMetadata.LastScannedAtUtc);
    }

    [Fact]
    public async Task BuildAsync_UsesModulePathForTopicWithDirectLessons()
    {
        var courseRoot = Path.Combine(_rootDirectory, "direct-module-course");
        var moduleDirectory = Path.Combine(courseRoot, "Modulo 02");
        Directory.CreateDirectory(moduleDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(moduleDirectory, "Aula 02.mp4"),
            Array.Empty<byte>());

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(new LocalFolderCourseBuildRequest
        {
            FolderPath = courseRoot
        });

        var detectedModule = Assert.Single(result.DetectedStructure.Modules);
        var detectedTopic = Assert.Single(detectedModule.Topics);
        var module = Assert.Single(result.Course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);

        Assert.Equal("Modulo 02", detectedModule.RelativePath);
        Assert.Equal(".", detectedTopic.RelativePath);
        Assert.Equal("Modulo 02", module.SourceRelativePath);
        Assert.Equal("Modulo 02", topic.SourceRelativePath);
        Assert.Equal("Modulo 02/Aula 02.mp4", lesson.RelativeFilePath);
    }

    [Fact]
    public async Task BuildAsync_UsesRootMarkerForLessonsDirectlyInCourseRoot()
    {
        var courseRoot = Path.Combine(_rootDirectory, "root-lessons-course");
        Directory.CreateDirectory(courseRoot);
        await File.WriteAllBytesAsync(
            Path.Combine(courseRoot, "Aula raiz.mp4"),
            Array.Empty<byte>());

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(new LocalFolderCourseBuildRequest
        {
            FolderPath = courseRoot
        });

        var detectedModule = Assert.Single(result.DetectedStructure.Modules);
        var detectedTopic = Assert.Single(detectedModule.Topics);
        var module = Assert.Single(result.Course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);

        Assert.Equal(".", detectedModule.RelativePath);
        Assert.Equal(".", detectedTopic.RelativePath);
        Assert.Equal(".", module.SourceRelativePath);
        Assert.Equal(".", topic.SourceRelativePath);
        Assert.Equal("Aula raiz.mp4", lesson.RelativeFilePath);
    }

    [Fact]
    public async Task ImportAndLoadAsync_RoundTripsRelativeFilePathThroughSqlite()
    {
        var (courseRoot, videoPath) = await CreateCourseFileAsync("persistence-course");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var contextFactory = new TestDbContextFactory(options);

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var importService = new LocalCourseImportService(
            contextFactory,
            new UnusedFolderPickerService(),
            CreateBuilder(),
            NullLogger<LocalCourseImportService>.Instance);

        var importResult = await importService.ImportFromFolderAsync(courseRoot);

        Assert.Equal(LocalCourseImportStatus.Imported, importResult.Status);
        Assert.True(importResult.CourseId.HasValue);

        var expectedAbsolutePath = Path.GetFullPath(videoPath);

        await using (var assertContext = new StudyHubDbContext(options))
        {
            var persistedModule = await assertContext.Modules.AsNoTracking().SingleAsync();
            var persistedTopic = await assertContext.Topics.AsNoTracking().SingleAsync();
            var persistedLesson = await assertContext.Lessons.AsNoTracking().SingleAsync();

            Assert.Equal("Modulo 01", persistedModule.SourceRelativePath);
            Assert.Equal("Modulo 01/Topico 01", persistedTopic.SourceRelativePath);
            Assert.Equal(expectedAbsolutePath, persistedLesson.LocalFilePath);
            Assert.Equal(ExpectedRelativePath, persistedLesson.RelativeFilePath);
        }

        var persistedCourseService = new PersistedCourseService(contextFactory);
        var loadedCourse = await persistedCourseService.GetCourseByIdAsync(importResult.CourseId.Value);

        Assert.NotNull(loadedCourse);
        var loadedModule = Assert.Single(loadedCourse!.Modules);
        var loadedTopic = Assert.Single(loadedModule.Topics);
        var loadedLesson = Assert.Single(loadedTopic.Lessons);

        Assert.Equal("Modulo 01", loadedModule.SourceRelativePath);
        Assert.Equal("Modulo 01/Topico 01", loadedTopic.SourceRelativePath);
        Assert.Equal(expectedAbsolutePath, loadedLesson.LocalFilePath);
        Assert.Equal(ExpectedRelativePath, loadedLesson.RelativeFilePath);
        Assert.DoesNotContain('\\', loadedLesson.RelativeFilePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private async Task<(string CourseRoot, string VideoPath)> CreateCourseFileAsync(string courseFolderName)
    {
        var courseRoot = Path.Combine(_rootDirectory, courseFolderName);
        var lessonDirectory = Path.Combine(courseRoot, "Modulo 01", "Topico 01");
        Directory.CreateDirectory(lessonDirectory);
        // A second root child keeps the scanner presentation root at the course root.
        Directory.CreateDirectory(Path.Combine(courseRoot, "empty-presentation-boundary"));

        var videoPath = Path.Combine(lessonDirectory, "Aula 01.mp4");
        await File.WriteAllBytesAsync(videoPath, Array.Empty<byte>());

        return (courseRoot, videoPath);
    }

    private static ILocalCourseScanner CreateScanner()
        => new LocalCourseScanner(new FakeVideoMetadataReader());

    private static LocalFolderCourseBuilder CreateBuilder()
        => new(CreateScanner());

    private sealed class FakeVideoMetadataReader : IVideoMetadataReader
    {
        public Task<TimeSpan?> TryReadDurationAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(5));
    }

    private sealed class CancelingVideoMetadataReader(
        CancellationTokenSource cancellationSource) : IVideoMetadataReader
    {
        private readonly CancellationTokenSource _cancellationSource = cancellationSource;

        public Task<TimeSpan?> TryReadDurationAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            _cancellationSource.Cancel();
            return Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(5));
        }
    }

    private sealed class UnusedFolderPickerService : IFolderPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class TestDbContextFactory(DbContextOptions<StudyHubDbContext> options)
        : IDbContextFactory<StudyHubDbContext>
    {
        private readonly DbContextOptions<StudyHubDbContext> _options = options;

        public StudyHubDbContext CreateDbContext()
            => new(_options);

        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new StudyHubDbContext(_options));
    }
}
