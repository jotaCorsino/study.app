using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.CourseSourceManagement;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class CourseSourceManagementServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "studyhub-course-source-management-tests",
        Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<StudyHubDbContext> _options;
    private readonly TestDbContextFactory _contextFactory;
    private readonly LocalFolderCourseBuilder _builder;
    private readonly CourseSourceManagementService _service;

    public CourseSourceManagementServiceTests()
    {
        Directory.CreateDirectory(_testRoot);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(_options);

        using (var context = new StudyHubDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        var scanner = new LocalCourseScanner(new FakeVideoMetadataReader());
        _builder = new LocalFolderCourseBuilder(scanner);
        _service = new CourseSourceManagementService(
            _contextFactory,
            scanner,
            NullLogger<CourseSourceManagementService>.Instance);
    }

    [Fact]
    public async Task ChangeLocationAsync_EquivalentMovedFolder_PreservesIdentityStateMetadataAndSnapshot()
    {
        var relativePaths = new[]
        {
            "Module 01/Topic 01/Lesson 01.mp4",
            "Module 01/Topic 01/Lesson 02.mp4"
        };
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("moved", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, relativePaths);
        var originalPersisted = await LoadCourseAsync(seed.CourseId);
        var originalModuleSourcePaths = originalPersisted.Modules
            .OrderBy(module => module.Order)
            .Select(module => module.SourceRelativePath)
            .ToArray();
        var originalTopicSourcePaths = originalPersisted.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .Select(topic => topic.SourceRelativePath)
            .ToArray();

        Assert.All(originalModuleSourcePaths, path => Assert.False(string.IsNullOrWhiteSpace(path)));
        Assert.All(originalTopicSourcePaths, path => Assert.False(string.IsNullOrWhiteSpace(path)));

        await CreateCourseFilesAsync(candidateRoot, relativePaths);

        var candidateScan = await _builder.BuildAsync(new LocalFolderCourseBuildRequest
        {
            FolderPath = candidateRoot
        });

        Assert.NotEqual(seed.OriginalScannerCourseId, candidateScan.DetectedStructure.CourseId);
        Assert.NotEqual(
            seed.OriginalScannerLessonIds,
            CandidateLessons(candidateScan.DetectedStructure).Select(lesson => lesson.LessonId).ToArray());

        var validation = await _service.ValidateLocationAsync(seed.CourseId, candidateRoot);

        Assert.True(validation.Success);
        Assert.Equal(CourseSourceLocationCompatibility.ExactMatch, validation.Compatibility);
        Assert.Equal(2, validation.ExistingLessonCount);
        Assert.Equal(2, validation.ExistingComparableLessonCount);
        Assert.Equal(2, validation.CandidateLessonCount);
        Assert.Equal(2, validation.MatchedExistingLessonCount);
        Assert.Equal(0, validation.MissingExistingLessonCount);
        Assert.Equal(0, validation.NewCandidateLessonCount);
        Assert.True(validation.CanApplyDirectly);

        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.True(result.Success);
        Assert.True(result.Applied);
        Assert.Equal(CourseSourceLocationChangeStatus.Changed, result.Status);

        var persisted = await LoadCourseAsync(seed.CourseId);
        var persistedModules = persisted.Modules.OrderBy(module => module.Order).ToList();
        var persistedTopics = persistedModules.SelectMany(module => module.Topics.OrderBy(topic => topic.Order)).ToList();
        var persistedLessons = persistedTopics.SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order)).ToList();

        Assert.Equal(seed.CourseId, persisted.Id);
        Assert.Equal(seed.ModuleIds, persistedModules.Select(module => module.Id).ToArray());
        Assert.Equal(seed.TopicIds, persistedTopics.Select(topic => topic.Id).ToArray());
        Assert.Equal(seed.LessonIds, persistedLessons.Select(lesson => lesson.Id).ToArray());
        Assert.Equal(originalModuleSourcePaths, persistedModules.Select(module => module.SourceRelativePath).ToArray());
        Assert.Equal(originalTopicSourcePaths, persistedTopics.Select(topic => topic.SourceRelativePath).ToArray());
        Assert.Equal("Edited course title", persisted.Title);
        Assert.Equal("Edited module title", persistedModules[0].Title);
        Assert.Equal("Edited topic title", persistedTopics[0].Title);
        Assert.Equal("Edited lesson 1", persistedLessons[0].Title);
        Assert.Equal(seed.CurrentLessonId, persisted.CurrentLessonId);
        Assert.Equal(seed.TopicCompletedAtUtc, persistedTopics[0].CompletedAtUtc);
        Assert.Equal(LessonStatus.InProgress, persistedLessons[0].Status);
        Assert.Equal(37.5, persistedLessons[0].WatchedPercentage);
        Assert.Equal(73, persistedLessons[0].LastPlaybackPositionSeconds);
        Assert.Equal(Path.GetFullPath(candidateRoot), persisted.FolderPath);

        for (var index = 0; index < persistedLessons.Count; index++)
        {
            Assert.Equal(relativePaths[index], persistedLessons[index].RelativeFilePath);
            var expectedAbsolutePath = Path.GetFullPath(
                Path.Combine(candidateRoot, relativePaths[index].Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(expectedAbsolutePath, persistedLessons[index].LocalFilePath);
            Assert.Equal(expectedAbsolutePath, persistedLessons[index].FilePath);
        }

        var metadata = Assert.IsType<JsonObject>(JsonNode.Parse(persisted.SourceMetadataJson));
        Assert.Equal(Path.GetFullPath(candidateRoot), metadata["rootPath"]!.GetValue<string>());
        Assert.Equal(seed.MetadataImportedAt, metadata["importedAt"]!.GetValue<DateTime>());
        Assert.Equal("local-folder-v1", metadata["scanVersion"]!.GetValue<string>());
        Assert.Equal("LocalFileSystem", metadata["provider"]!.GetValue<string>());
        Assert.True(metadata["introSkipEnabled"]!.GetValue<bool>());
        Assert.Equal(12, metadata["introSkipSeconds"]!.GetValue<int>());
        Assert.Equal("preserve-me", metadata["futureSetting"]!.GetValue<string>());

        var snapshot = await LoadSnapshotAsync(seed.CourseId);
        Assert.Equal(Path.GetFullPath(candidateRoot), snapshot.RootFolderPath);
        Assert.Equal(seed.SnapshotImportedAt, snapshot.ImportedAt);

        var manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(snapshot.StructureJson, JsonOptions);
        Assert.NotNull(manifest);
        Assert.Equal(seed.CourseId, manifest!.CourseId);
        Assert.Equal(seed.ModuleIds, manifest.Modules.Select(module => module.ModuleId).ToArray());
        Assert.Equal(seed.TopicIds, manifest.Modules.SelectMany(module => module.Topics).Select(topic => topic.TopicId).ToArray());
        Assert.Equal(seed.LessonIds, CandidateLessons(manifest).Select(lesson => lesson.LessonId).ToArray());
        Assert.Equal(Path.GetFullPath(candidateRoot), manifest.RootFolderPath);
        Assert.Equal("CourseA", manifest.RootFolderName);

        foreach (var lesson in CandidateLessons(manifest).Concat(RootNodeLessons(manifest.RootNode)))
        {
            var expectedAbsolutePath = Path.GetFullPath(
                Path.Combine(candidateRoot, lesson.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(expectedAbsolutePath, lesson.AbsolutePath);
        }

        var loadedCourse = await new PersistedCourseService(_contextFactory).GetCourseByIdAsync(seed.CourseId);
        Assert.NotNull(loadedCourse);
        Assert.Equal(Path.GetFullPath(candidateRoot), loadedCourse!.SourceMetadata.RootPath);
        Assert.Equal(Path.GetFullPath(candidateRoot), loadedCourse.FolderPath);
        Assert.Equal(seed.LessonIds, loadedCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Select(lesson => lesson.Id)
            .ToArray());
        Assert.Equal(seed.TopicCompletedAtUtc, loadedCourse.Modules[0].Topics[0].CompletedAtUtc);
    }

    [Fact]
    public async Task ChangeLocationAsync_FolderWithAdditionalContent_DetectsButDoesNotPersistNewLessons()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        const string newLesson = "Module 02/Topic 02/Lesson 02.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("expanded", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        await CreateCourseFilesAsync(candidateRoot, knownLesson, newLesson);

        var validation = await _service.ValidateLocationAsync(seed.CourseId, candidateRoot);

        Assert.Equal(CourseSourceLocationCompatibility.CompatibleWithNewContent, validation.Compatibility);
        Assert.Equal(1, validation.MatchedExistingLessonCount);
        Assert.Equal(1, validation.NewCandidateLessonCount);
        Assert.Equal(0, validation.MissingExistingLessonCount);

        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationChangeStatus.Changed, result.Status);
        var persisted = await LoadCourseAsync(seed.CourseId);
        var lesson = Assert.Single(persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        Assert.Equal(seed.LessonIds[0], lesson.Id);
        Assert.Equal(knownLesson, lesson.RelativeFilePath);
        Assert.DoesNotContain(
            persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons),
            item => string.Equals(item.RelativeFilePath, newLesson, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangeLocationAsync_WrongFolder_IsIncompatibleAndLeavesDatabaseUntouched()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("wrong", "CourseB");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        await CreateCourseFilesAsync(candidateRoot, "Another module/Another topic/Other.mp4");
        var snapshotBefore = await LoadSnapshotAsync(seed.CourseId);

        var validation = await _service.ValidateLocationAsync(seed.CourseId, candidateRoot);
        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationCompatibility.Incompatible, validation.Compatibility);
        Assert.Equal(0, validation.MatchedExistingLessonCount);
        Assert.Equal(CourseSourceLocationChangeStatus.Blocked, result.Status);
        var persisted = await LoadCourseAsync(seed.CourseId);
        var persistedLesson = Assert.Single(persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        Assert.Equal(Path.GetFullPath(originalRoot), persisted.FolderPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(originalRoot, knownLesson.Replace('/', Path.DirectorySeparatorChar))), persistedLesson.LocalFilePath);
        var snapshotAfter = await LoadSnapshotAsync(seed.CourseId);
        Assert.Equal(snapshotBefore.RootFolderPath, snapshotAfter.RootFolderPath);
        Assert.Equal(snapshotBefore.StructureJson, snapshotAfter.StructureJson);
    }

    [Fact]
    public async Task ChangeLocationAsync_PartialMatch_RequiresConfirmationAndKeepsMissingLessons()
    {
        var relativePaths = new[]
        {
            "Module 01/Topic 01/Lesson 01.mp4",
            "Module 01/Topic 01/Lesson 02.mp4"
        };
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("partial", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, relativePaths);
        await CreateCourseFilesAsync(candidateRoot, relativePaths[0]);

        var validation = await _service.ValidateLocationAsync(seed.CourseId, candidateRoot);
        Assert.Equal(CourseSourceLocationCompatibility.PartialMatch, validation.Compatibility);
        Assert.Equal(1, validation.MatchedExistingLessonCount);
        Assert.Equal(1, validation.MissingExistingLessonCount);
        Assert.True(validation.RequiresExplicitConfirmation);

        var blocked = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationChangeStatus.PartialConfirmationRequired, blocked.Status);
        Assert.Equal(Path.GetFullPath(originalRoot), (await LoadCourseAsync(seed.CourseId)).FolderPath);

        var changed = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot,
            ConfirmPartialMatch = true
        });

        Assert.Equal(CourseSourceLocationChangeStatus.Changed, changed.Status);
        var persisted = await LoadCourseAsync(seed.CourseId);
        var lessons = persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).OrderBy(lesson => lesson.Order).ToList();
        Assert.Equal(2, lessons.Count);
        Assert.Equal(seed.LessonIds, lessons.Select(lesson => lesson.Id).ToArray());
        Assert.Equal(LessonStatus.Completed, lessons[1].Status);
        Assert.Equal(100, lessons[1].WatchedPercentage);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(candidateRoot, relativePaths[1].Replace('/', Path.DirectorySeparatorChar))),
            lessons[1].LocalFilePath);
        Assert.False(File.Exists(lessons[1].LocalFilePath));
    }

    [Fact]
    public async Task ChangeLocationAsync_UnsafeStoredRelativePath_DoesNotInventLegacyPathsOrTriggerRehydration()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("moved", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        await CreateCourseFilesAsync(candidateRoot, knownLesson);
        var oldAbsolutePath = Path.GetFullPath(
            Path.Combine(originalRoot, knownLesson.Replace('/', Path.DirectorySeparatorChar)));

        await using (var context = new StudyHubDbContext(_options))
        {
            var lesson = await context.Lessons.SingleAsync(record => record.Id == seed.LessonIds[0]);
            lesson.RelativeFilePath = "../outside.mp4";
            await context.SaveChangesAsync();
        }

        var validation = await _service.ValidateLocationAsync(seed.CourseId, candidateRoot);
        Assert.Equal(CourseSourceLocationCompatibility.ExactMatch, validation.Compatibility);

        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationChangeStatus.Changed, result.Status);
        var persisted = await LoadCourseAsync(seed.CourseId);
        var persistedLesson = Assert.Single(persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        Assert.Equal("../outside.mp4", persistedLesson.RelativeFilePath);
        Assert.Equal(oldAbsolutePath, persistedLesson.LocalFilePath);
        Assert.Equal(oldAbsolutePath, persistedLesson.FilePath);

        var loadedCourse = await new PersistedCourseService(_contextFactory).GetCourseByIdAsync(seed.CourseId);
        var loadedLesson = Assert.Single(loadedCourse!.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        Assert.Equal("../outside.mp4", loadedLesson.RelativeFilePath);
        Assert.Equal(oldAbsolutePath, loadedLesson.LocalFilePath);
        Assert.Equal(seed.TopicCompletedAtUtc, loadedCourse.Modules[0].Topics[0].CompletedAtUtc);
    }

    [Fact]
    public async Task ChangeLocationAsync_InvalidManifest_UpdatesSafeFieldsAndPreservesInvalidJson()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        const string invalidManifest = "{not-valid-json";
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("moved", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        await CreateCourseFilesAsync(candidateRoot, knownLesson);

        await using (var context = new StudyHubDbContext(_options))
        {
            var snapshot = await context.CourseImportSnapshots.SingleAsync(record => record.CourseId == seed.CourseId);
            snapshot.StructureJson = invalidManifest;
            await context.SaveChangesAsync();
        }

        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationChangeStatus.Changed, result.Status);
        var persisted = await LoadCourseAsync(seed.CourseId);
        Assert.Equal(Path.GetFullPath(candidateRoot), persisted.FolderPath);
        Assert.Single(persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons));
        var snapshotAfter = await LoadSnapshotAsync(seed.CourseId);
        Assert.Equal(Path.GetFullPath(candidateRoot), snapshotAfter.RootFolderPath);
        Assert.Equal(invalidManifest, snapshotAfter.StructureJson);
        Assert.Equal(seed.SnapshotImportedAt, snapshotAfter.ImportedAt);

        var loadedCourse = await new PersistedCourseService(_contextFactory).GetCourseByIdAsync(seed.CourseId);
        Assert.NotNull(loadedCourse);
        Assert.Equal(Path.GetFullPath(candidateRoot), loadedCourse!.SourceMetadata.RootPath);
        Assert.Equal(seed.TopicCompletedAtUtc, loadedCourse.Modules[0].Topics[0].CompletedAtUtc);
        var repairedSnapshot = await LoadSnapshotAsync(seed.CourseId);
        var repairedManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(repairedSnapshot.StructureJson, JsonOptions);
        Assert.NotNull(repairedManifest);
        Assert.Equal(Path.GetFullPath(candidateRoot), repairedManifest!.RootFolderPath);
        Assert.Equal(seed.SnapshotImportedAt, repairedSnapshot.ImportedAt);
    }

    [Fact]
    public async Task ChangeLocationAsync_SemanticallyInvalidManifest_IsRebuiltFromAuthoritativeCourseOnLoad()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("moved", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        await CreateCourseFilesAsync(candidateRoot, knownLesson);

        await using (var context = new StudyHubDbContext(_options))
        {
            var snapshot = await context.CourseImportSnapshots.SingleAsync(record => record.CourseId == seed.CourseId);
            var malformedManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(snapshot.StructureJson, JsonOptions)!;
            malformedManifest.Modules.Add(new DetectedModuleStructure
            {
                ModuleId = Guid.NewGuid(),
                Order = 99,
                RawName = "Empty stale module"
            });
            snapshot.StructureJson = JsonSerializer.Serialize(malformedManifest, JsonOptions);
            await context.SaveChangesAsync();
        }

        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationChangeStatus.Changed, result.Status);
        var snapshotBeforeLoad = await LoadSnapshotAsync(seed.CourseId);
        Assert.Equal(Path.GetFullPath(candidateRoot), snapshotBeforeLoad.RootFolderPath);
        Assert.Equal(2, JsonSerializer.Deserialize<DetectedCourseStructure>(
            snapshotBeforeLoad.StructureJson,
            JsonOptions)!.Modules.Count);

        var loadedCourse = await new PersistedCourseService(_contextFactory).GetCourseByIdAsync(seed.CourseId);

        Assert.NotNull(loadedCourse);
        Assert.Equal(Path.GetFullPath(candidateRoot), loadedCourse!.SourceMetadata.RootPath);
        Assert.Equal(seed.ModuleIds, loadedCourse.Modules.Select(module => module.Id).ToArray());
        Assert.Equal(seed.LessonIds, loadedCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Select(lesson => lesson.Id)
            .ToArray());
        Assert.Equal(seed.TopicCompletedAtUtc, loadedCourse.Modules[0].Topics[0].CompletedAtUtc);
        var repairedSnapshot = await LoadSnapshotAsync(seed.CourseId);
        var repairedManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(repairedSnapshot.StructureJson, JsonOptions)!;
        Assert.Single(repairedManifest.Modules);
        Assert.Equal(Path.GetFullPath(candidateRoot), repairedManifest.RootFolderPath);
        Assert.Equal(seed.SnapshotImportedAt, repairedSnapshot.ImportedAt);
    }

    [Fact]
    public async Task ChangeLocationAsync_EmptyFolder_IsBlockedWithNoVideosFound()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var emptyRoot = CourseRoot("empty", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        Directory.CreateDirectory(emptyRoot);

        var validation = await _service.ValidateLocationAsync(seed.CourseId, emptyRoot);
        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = emptyRoot
        });

        Assert.False(validation.Success);
        Assert.Equal(CourseSourceLocationErrorKind.NoVideosFound, validation.ErrorKind);
        Assert.Equal(CourseSourceLocationChangeStatus.ValidationFailed, result.Status);
        Assert.Equal(Path.GetFullPath(originalRoot), (await LoadCourseAsync(seed.CourseId)).FolderPath);
    }

    [Fact]
    public async Task ValidateLocationAsync_MissingCourse_ReturnsControlledResult()
    {
        var candidateRoot = CourseRoot("candidate", "CourseA");
        await CreateCourseFilesAsync(candidateRoot, "Module 01/Topic 01/Lesson 01.mp4");

        var validation = await _service.ValidateLocationAsync(Guid.NewGuid(), candidateRoot);

        Assert.False(validation.Success);
        Assert.Equal(CourseSourceLocationErrorKind.CourseNotFound, validation.ErrorKind);
        Assert.Equal(CourseSourceLocationCompatibility.NotEvaluated, validation.Compatibility);
    }

    [Fact]
    public async Task ChangeLocationAsync_CurrentFolder_IsSafeIdempotentNoOp()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var staleRoot = CourseRoot("stale", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);

        await using (var context = new StudyHubDbContext(_options))
        {
            var snapshot = await context.CourseImportSnapshots.SingleAsync(record => record.CourseId == seed.CourseId);
            var staleManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(snapshot.StructureJson, JsonOptions)!;
            staleManifest.RootFolderPath = staleRoot;
            foreach (var lesson in CandidateLessons(staleManifest).Concat(RootNodeLessons(staleManifest.RootNode)))
            {
                lesson.AbsolutePath = Path.GetFullPath(
                    Path.Combine(staleRoot, lesson.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            snapshot.RootFolderPath = staleRoot;
            snapshot.StructureJson = JsonSerializer.Serialize(staleManifest, JsonOptions);
            await context.SaveChangesAsync();
        }

        var validation = await _service.ValidateLocationAsync(seed.CourseId, originalRoot + Path.DirectorySeparatorChar);
        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = originalRoot + Path.DirectorySeparatorChar
        });

        Assert.True(validation.IsCurrentLocation);
        Assert.Equal(CourseSourceLocationCompatibility.ExactMatch, validation.Compatibility);
        Assert.False(validation.CanApplyDirectly);
        Assert.Equal(CourseSourceLocationChangeStatus.Unchanged, result.Status);
        Assert.True(result.Success);
        Assert.False(result.Applied);
        var persisted = await LoadCourseAsync(seed.CourseId);
        Assert.Equal(seed.LessonIds, persisted.Modules.SelectMany(module => module.Topics).SelectMany(topic => topic.Lessons).Select(lesson => lesson.Id).ToArray());
        var repairedSnapshot = await LoadSnapshotAsync(seed.CourseId);
        Assert.Equal(Path.GetFullPath(originalRoot), repairedSnapshot.RootFolderPath);
        Assert.Equal(seed.SnapshotImportedAt, repairedSnapshot.ImportedAt);
        var repairedManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(repairedSnapshot.StructureJson, JsonOptions)!;
        Assert.Equal(Path.GetFullPath(originalRoot), repairedManifest.RootFolderPath);
        Assert.All(
            CandidateLessons(repairedManifest).Concat(RootNodeLessons(repairedManifest.RootNode)),
            lesson => Assert.Equal(
                Path.GetFullPath(Path.Combine(originalRoot, lesson.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
                lesson.AbsolutePath));
    }

    [Fact]
    public async Task ChangeLocationAsync_SnapshotIdentityPathMappingMismatch_IsBlockedAtomically()
    {
        var relativePaths = new[]
        {
            "Module 01/Topic 01/Lesson 01.mp4",
            "Module 01/Topic 01/Lesson 02.mp4"
        };
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("moved", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, relativePaths);
        await CreateCourseFilesAsync(candidateRoot, relativePaths);
        string mismatchedStructureJson;

        await using (var context = new StudyHubDbContext(_options))
        {
            var snapshot = await context.CourseImportSnapshots.SingleAsync(record => record.CourseId == seed.CourseId);
            var mismatchedManifest = JsonSerializer.Deserialize<DetectedCourseStructure>(snapshot.StructureJson, JsonOptions)!;
            var lessons = CandidateLessons(mismatchedManifest).ToArray();
            (lessons[0].LessonId, lessons[1].LessonId) = (lessons[1].LessonId, lessons[0].LessonId);
            mismatchedStructureJson = JsonSerializer.Serialize(mismatchedManifest, JsonOptions);
            snapshot.StructureJson = mismatchedStructureJson;
            await context.SaveChangesAsync();
        }

        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationChangeStatus.Failed, result.Status);
        Assert.Equal(CourseSourceLocationErrorKind.PersistenceFailed, result.ErrorKind);
        Assert.Equal(Path.GetFullPath(originalRoot), (await LoadCourseAsync(seed.CourseId)).FolderPath);
        var snapshotAfter = await LoadSnapshotAsync(seed.CourseId);
        Assert.Equal(Path.GetFullPath(originalRoot), snapshotAfter.RootFolderPath);
        Assert.Equal(mismatchedStructureJson, snapshotAfter.StructureJson);
    }

    [Fact]
    public async Task ChangeLocationAsync_NoComparableExistingPaths_IsBlockedAsInsufficientReferenceData()
    {
        const string knownLesson = "Module 01/Topic 01/Lesson 01.mp4";
        var originalRoot = CourseRoot("original", "CourseA");
        var candidateRoot = CourseRoot("moved", "CourseA");
        var seed = await SeedCourseAsync(originalRoot, knownLesson);
        await CreateCourseFilesAsync(candidateRoot, knownLesson);
        var outsidePath = Path.Combine(_testRoot, "outside", "Lesson 01.mp4");

        await using (var context = new StudyHubDbContext(_options))
        {
            var lesson = await context.Lessons.SingleAsync(record => record.Id == seed.LessonIds[0]);
            lesson.RelativeFilePath = string.Empty;
            lesson.LocalFilePath = outsidePath;
            lesson.FilePath = outsidePath;
            await context.SaveChangesAsync();
        }

        var validation = await _service.ValidateLocationAsync(seed.CourseId, candidateRoot);
        var result = await _service.ChangeLocationAsync(new ChangeCourseSourceLocationRequest
        {
            CourseId = seed.CourseId,
            FolderPath = candidateRoot
        });

        Assert.Equal(CourseSourceLocationCompatibility.InsufficientReferenceData, validation.Compatibility);
        Assert.Equal(0, validation.ExistingComparableLessonCount);
        Assert.Equal(CourseSourceLocationChangeStatus.Blocked, result.Status);
        Assert.Equal(Path.GetFullPath(originalRoot), (await LoadCourseAsync(seed.CourseId)).FolderPath);
    }

    public void Dispose()
    {
        _connection.Dispose();

        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private string CourseRoot(string parentName, string courseName)
        => Path.Combine(_testRoot, parentName, courseName);

    private async Task<SeededCourse> SeedCourseAsync(string rootPath, params string[] relativePaths)
    {
        await CreateCourseFilesAsync(rootPath, relativePaths);
        var scanResult = await _builder.BuildAsync(new LocalFolderCourseBuildRequest
        {
            FolderPath = rootPath
        });
        var manifest = scanResult.DetectedStructure;
        var originalScannerCourseId = manifest.CourseId;
        var originalScannerLessonIds = CandidateLessons(manifest).Select(lesson => lesson.LessonId).ToArray();
        var courseId = Guid.NewGuid();
        var lessonIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var moduleIds = new List<Guid>();
        var topicIds = new List<Guid>();
        var lessonIds = new List<Guid>();

        manifest.CourseId = courseId;
        foreach (var module in manifest.Modules.OrderBy(item => item.Order))
        {
            module.ModuleId = Guid.NewGuid();
            moduleIds.Add(module.ModuleId);

            foreach (var topic in module.Topics.OrderBy(item => item.Order))
            {
                topic.TopicId = Guid.NewGuid();
                topicIds.Add(topic.TopicId);

                foreach (var lesson in topic.Lessons.OrderBy(item => item.Order))
                {
                    lesson.LessonId = Guid.NewGuid();
                    lessonIds.Add(lesson.LessonId);
                    lessonIdsByPath[lesson.RelativePath] = lesson.LessonId;
                }
            }
        }

        foreach (var lesson in RootNodeLessons(manifest.RootNode))
        {
            if (lessonIdsByPath.TryGetValue(lesson.RelativePath, out var lessonId))
            {
                lesson.LessonId = lessonId;
            }
        }

        var metadataImportedAt = new DateTime(2026, 4, 1, 10, 30, 0, DateTimeKind.Utc);
        var snapshotImportedAt = new DateTime(2026, 4, 1, 10, 31, 0, DateTimeKind.Utc);
        var topicCompletedAtUtc = new DateTime(2026, 4, 2, 11, 0, 0, DateTimeKind.Utc);
        var metadata = new JsonObject
        {
            ["rootPath"] = Path.GetFullPath(rootPath),
            ["importedAt"] = JsonValue.Create(metadataImportedAt),
            ["scanVersion"] = "local-folder-v1",
            ["provider"] = "LocalFileSystem",
            ["introSkipEnabled"] = true,
            ["introSkipSeconds"] = 12,
            ["futureSetting"] = "preserve-me"
        };
        var lessonIndex = 0;
        var courseRecord = new CourseRecord
        {
            Id = courseId,
            RawTitle = "Raw course title",
            RawDescription = "Raw course description",
            Title = "Edited course title",
            Description = "Edited course description",
            Category = "Local course",
            ThumbnailUrl = "thumb.png",
            FolderPath = Path.GetFullPath(rootPath),
            SourceType = CourseSourceType.LocalFolder,
            LifecycleStatus = CourseLifecycleStatus.Active,
            SourceMetadataJson = metadata.ToJsonString(JsonOptions),
            TotalDurationMinutes = relativePaths.Length * 5,
            AddedAt = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc),
            LastAccessedAt = new DateTime(2026, 4, 3, 9, 0, 0, DateTimeKind.Utc),
            CurrentLessonId = lessonIds[0],
            Modules = manifest.Modules.OrderBy(module => module.Order).Select(module => new ModuleRecord
            {
                Id = module.ModuleId,
                CourseId = courseId,
                Order = module.Order,
                RawTitle = module.RawName,
                RawDescription = "Raw module description",
                Title = "Edited module title",
                Description = "Edited module description",
                SourceRelativePath = NormalizeStructuralPath(module.RelativePath),
                Topics = module.Topics.OrderBy(topic => topic.Order).Select(topic => new TopicRecord
                {
                    Id = topic.TopicId,
                    ModuleId = module.ModuleId,
                    Order = topic.Order,
                    RawTitle = topic.RawName,
                    RawDescription = "Raw topic description",
                    Title = "Edited topic title",
                    Description = "Edited topic description",
                    SourceRelativePath = ComposeStructuralPath(module.RelativePath, topic.RelativePath),
                    CompletedAtUtc = topicCompletedAtUtc,
                    Lessons = topic.Lessons.OrderBy(lesson => lesson.Order).Select(lesson =>
                    {
                        var index = lessonIndex++;
                        return new LessonRecord
                        {
                            Id = lesson.LessonId,
                            TopicId = topic.TopicId,
                            Order = lesson.Order,
                            RawTitle = lesson.RawName,
                            RawDescription = "Raw lesson description",
                            Title = $"Edited lesson {index + 1}",
                            Description = "Edited lesson description",
                            FilePath = lesson.AbsolutePath,
                            SourceType = LessonSourceType.LocalFile,
                            LocalFilePath = lesson.AbsolutePath,
                            RelativeFilePath = lesson.RelativePath,
                            Provider = "LocalFileSystem",
                            DurationMinutes = 5,
                            Status = index == 0 ? LessonStatus.InProgress : LessonStatus.Completed,
                            WatchedPercentage = index == 0 ? 37.5 : 100,
                            LastPlaybackPositionSeconds = index == 0 ? 73 : 300
                        };
                    }).ToList()
                }).ToList()
            }).ToList()
        };

        var snapshot = new CourseImportSnapshotRecord
        {
            CourseId = courseId,
            SourceKind = "LocalFolder",
            RootFolderPath = Path.GetFullPath(rootPath),
            StructureJson = JsonSerializer.Serialize(manifest, JsonOptions),
            ImportedAt = snapshotImportedAt
        };

        await using (var context = new StudyHubDbContext(_options))
        {
            context.Courses.Add(courseRecord);
            context.CourseImportSnapshots.Add(snapshot);
            await context.SaveChangesAsync();
        }

        return new SeededCourse(
            courseId,
            originalScannerCourseId,
            originalScannerLessonIds,
            moduleIds.ToArray(),
            topicIds.ToArray(),
            lessonIds.ToArray(),
            lessonIds[0],
            metadataImportedAt,
            snapshotImportedAt,
            topicCompletedAtUtc);
    }

    private static async Task CreateCourseFilesAsync(string rootPath, params string[] relativePaths)
    {
        Directory.CreateDirectory(rootPath);

        foreach (var relativePath in relativePaths)
        {
            var absolutePath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllBytesAsync(absolutePath, []);
        }
    }

    private static string ComposeStructuralPath(string moduleRelativePath, string topicRelativePath)
    {
        var modulePath = NormalizeStructuralPath(moduleRelativePath);
        var topicPath = NormalizeStructuralPath(topicRelativePath);

        if (topicPath == ".")
        {
            return modulePath;
        }

        return modulePath == "." ? topicPath : $"{modulePath}/{topicPath}";
    }

    private static string NormalizeStructuralPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized) || normalized == "." ? "." : normalized;
    }

    private async Task<CourseRecord> LoadCourseAsync(Guid courseId)
    {
        await using var context = new StudyHubDbContext(_options);
        return await context.Courses
            .AsNoTracking()
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

    private static IEnumerable<DetectedLessonFile> CandidateLessons(DetectedCourseStructure structure)
        => structure.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order));

    private static IEnumerable<DetectedLessonFile> RootNodeLessons(DetectedFolderNode node)
    {
        foreach (var lesson in node.DirectLessons)
        {
            yield return lesson;
        }

        foreach (var child in node.Children)
        {
            foreach (var lesson in RootNodeLessons(child))
            {
                yield return lesson;
            }
        }
    }

    private sealed class FakeVideoMetadataReader : IVideoMetadataReader
    {
        public Task<TimeSpan?> TryReadDurationAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(5));
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

    private sealed record SeededCourse(
        Guid CourseId,
        Guid OriginalScannerCourseId,
        Guid[] OriginalScannerLessonIds,
        Guid[] ModuleIds,
        Guid[] TopicIds,
        Guid[] LessonIds,
        Guid CurrentLessonId,
        DateTime MetadataImportedAt,
        DateTime SnapshotImportedAt,
        DateTime TopicCompletedAtUtc);
}
