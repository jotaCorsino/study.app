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
    public async Task BuildAsync_PreservesAbsoluteAndPortableRelativeLessonPaths()
    {
        var (courseRoot, videoPath) = await CreateCourseFileAsync("builder-course");
        var builder = new LocalFolderCourseBuilder(new FakeVideoMetadataReader());

        var result = await builder.BuildAsync(new LocalFolderCourseBuildRequest
        {
            FolderPath = courseRoot
        });

        var detectedLesson = Assert.Single(result.DetectedStructure.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons));
        var lesson = Assert.Single(result.Course.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons));
        var expectedAbsolutePath = Path.GetFullPath(videoPath);

        Assert.Equal(expectedAbsolutePath, detectedLesson.AbsolutePath);
        Assert.Equal(ExpectedRelativePath, detectedLesson.RelativePath);
        Assert.Equal(expectedAbsolutePath, lesson.LocalFilePath);
        Assert.True(Path.IsPathFullyQualified(lesson.LocalFilePath));
        Assert.Equal(ExpectedRelativePath, lesson.RelativeFilePath);
        Assert.False(Path.IsPathFullyQualified(lesson.RelativeFilePath));
        Assert.DoesNotContain('\\', lesson.RelativeFilePath);
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
            new LocalFolderCourseBuilder(new FakeVideoMetadataReader()),
            NullLogger<LocalCourseImportService>.Instance);

        var importResult = await importService.ImportFromFolderAsync(courseRoot);

        Assert.Equal(LocalCourseImportStatus.Imported, importResult.Status);
        Assert.True(importResult.CourseId.HasValue);

        var expectedAbsolutePath = Path.GetFullPath(videoPath);

        await using (var assertContext = new StudyHubDbContext(options))
        {
            var persistedLesson = await assertContext.Lessons.AsNoTracking().SingleAsync();

            Assert.Equal(expectedAbsolutePath, persistedLesson.LocalFilePath);
            Assert.Equal(ExpectedRelativePath, persistedLesson.RelativeFilePath);
        }

        var persistedCourseService = new PersistedCourseService(contextFactory);
        var loadedCourse = await persistedCourseService.GetCourseByIdAsync(importResult.CourseId.Value);

        Assert.NotNull(loadedCourse);
        var loadedLesson = Assert.Single(loadedCourse!.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons));

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

        var videoPath = Path.Combine(lessonDirectory, "Aula 01.mp4");
        await File.WriteAllBytesAsync(videoPath, Array.Empty<byte>());

        return (courseRoot, videoPath);
    }

    private sealed class FakeVideoMetadataReader : IVideoMetadataReader
    {
        public Task<TimeSpan?> TryReadDurationAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(5));
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
