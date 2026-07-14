using studyhub.application.Contracts.CourseContentSync;
using studyhub.application.Contracts.LocalImport;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class CourseContentSyncPlannerTests
{
    private static readonly DateTime ScannedAtUtc = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string RootPath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "studyhub-sync-planner"));

    [Fact]
    public void Plan_EqualPathsIgnoresScannerIdsAndMetadataChanges()
    {
        var existing = CreateStandardCourse();
        var existingModule = existing.Modules.Single();
        var existingTopic = existingModule.Topics.Single();
        var existingLesson = existingTopic.Lessons.Single();
        var detected = CreateStandardDetection();
        detected.CourseId = Guid.NewGuid();
        detected.Modules[0].ModuleId = Guid.NewGuid();
        detected.Modules[0].Topics[0].TopicId = Guid.NewGuid();
        detected.Modules[0].Topics[0].Lessons[0].LessonId = Guid.NewGuid();
        detected.Modules[0].Topics[0].Lessons[0].RawName = "Nome físico alterado";
        detected.Modules[0].Topics[0].Lessons[0].Duration = TimeSpan.FromHours(9);
        detected.Modules[0].Topics[0].Lessons[0].FileSizeBytes = 987_654;

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, result.Status);
        Assert.True(result.Success);
        Assert.False(result.HasChanges);
        Assert.False(result.CanApply);
        Assert.All(result.Modules, item => Assert.Equal(CourseContentSyncChangeKind.Unchanged, item.ChangeKind));
        Assert.All(result.Topics, item => Assert.Equal(CourseContentSyncChangeKind.Unchanged, item.ChangeKind));
        Assert.All(result.Lessons, item => Assert.Equal(CourseContentSyncChangeKind.Unchanged, item.ChangeKind));
        Assert.Equal(existingModule.Id, result.Modules.Single().ExistingModuleId);
        Assert.Equal(existingTopic.Id, result.Topics.Single().ExistingTopicId);
        Assert.Equal(existingLesson.Id, result.Lessons.Single().ExistingLessonId);
        Assert.Equal("Aula persistida", result.Lessons.Single().Title);
        Assert.Equal("Nome físico alterado", result.Lessons.Single().DetectedName);
        Assert.Equal(TimeSpan.FromHours(9), result.Lessons.Single().DetectedDuration);
        Assert.Equal(987_654, result.Lessons.Single().DetectedFileSizeBytes);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_NewModuleMarksItsEntireDetectedTreeAsNewWithoutPersistentIds()
    {
        var existing = CreateStandardCourse();
        var detected = CreateStandardDetection();
        detected.Modules.Add(CreateDetectedModule(
            "Modulo 02",
            2,
            CreateDetectedTopic(
                ".",
                1,
                CreateDetectedLesson("Modulo 02/Aula 02.mp4", 1))));

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.Ready, result.Status);
        var newModule = Assert.Single(result.Modules, item => item.ChangeKind == CourseContentSyncChangeKind.New);
        var newTopic = Assert.Single(result.Topics, item => item.ChangeKind == CourseContentSyncChangeKind.New);
        var newLesson = Assert.Single(result.Lessons, item => item.ChangeKind == CourseContentSyncChangeKind.New);
        Assert.Null(newModule.ExistingModuleId);
        Assert.Null(newTopic.ExistingModuleId);
        Assert.Null(newTopic.ExistingTopicId);
        Assert.Null(newLesson.ExistingTopicId);
        Assert.Null(newLesson.ExistingLessonId);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_NewTopicUsesExistingModuleIdAndMarksItsLessonsNew()
    {
        var existing = CreateStandardCourse();
        var detected = CreateStandardDetection();
        detected.Modules[0].Topics.Add(CreateDetectedTopic(
            "Topico 02",
            2,
            CreateDetectedLesson("Modulo 01/Topico 02/Aula 02.mp4", 1)));

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(CourseContentSyncChangeKind.Unchanged, result.Modules.Single().ChangeKind);
        var topic = Assert.Single(result.Topics, item => item.ChangeKind == CourseContentSyncChangeKind.New);
        var lesson = Assert.Single(result.Lessons, item => item.ChangeKind == CourseContentSyncChangeKind.New);
        Assert.Equal(existing.Modules[0].Id, topic.ExistingModuleId);
        Assert.Null(topic.ExistingTopicId);
        Assert.Null(lesson.ExistingTopicId);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_NewLessonUsesExistingTopicId()
    {
        var existing = CreateStandardCourse();
        var detected = CreateStandardDetection();
        detected.Modules[0].Topics[0].Lessons.Add(
            CreateDetectedLesson("Modulo 01/Topico 01/Aula 02.mp4", 2));

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        var lesson = Assert.Single(result.Lessons, item => item.ChangeKind == CourseContentSyncChangeKind.New);
        Assert.Equal(existing.Modules[0].Topics[0].Id, lesson.ExistingTopicId);
        Assert.Null(lesson.ExistingLessonId);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_MissingLessonIsReportedWithoutChangingItsParents()
    {
        var existing = CreateStandardCourse();
        var topic = existing.Modules[0].Topics[0];
        var missingLesson = CreateLesson(
            topic.Id,
            "Modulo 01/Topico 01/Aula 02.mp4",
            2);
        topic.Lessons.Add(missingLesson);

        var result = new CourseContentSyncPlanner().Plan(
            existing,
            CreateStandardDetection(),
            RootPath);

        Assert.Equal(CourseContentSyncChangeKind.Unchanged, result.Modules.Single().ChangeKind);
        Assert.Equal(CourseContentSyncChangeKind.Unchanged, result.Topics.Single().ChangeKind);
        Assert.Equal(
            CourseContentSyncChangeKind.Missing,
            result.Lessons.Single(item => item.ExistingLessonId == missingLesson.Id).ChangeKind);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_MissingTopicKeepsModuleUnchangedAndCascadesToLessons()
    {
        var existing = CreateStandardCourse();
        var module = existing.Modules.Single();
        var missingTopic = CreateTopic(
            module.Id,
            "Modulo 01/Topico 02",
            2,
            CreateLesson(Guid.NewGuid(), "Modulo 01/Topico 02/Aula 02.mp4", 1));
        missingTopic.Lessons[0].TopicId = missingTopic.Id;
        module.Topics.Add(missingTopic);
        var detected = CreateStandardDetection();

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(CourseContentSyncChangeKind.Unchanged, result.Modules.Single().ChangeKind);
        Assert.Equal(CourseContentSyncChangeKind.Missing, result.Topics.Single(item => item.ExistingTopicId == missingTopic.Id).ChangeKind);
        Assert.Equal(CourseContentSyncChangeKind.Missing, result.Lessons.Single(item => item.ExistingTopicId == missingTopic.Id).ChangeKind);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_MissingModuleCascadesToAllPersistedDescendants()
    {
        var existing = CreateStandardCourse();
        var missingModule = CreateModule(
            existing.Id,
            "Modulo 02",
            2,
            CreateTopic(Guid.NewGuid(), "Modulo 02", 1));
        missingModule.Topics[0].ModuleId = missingModule.Id;
        missingModule.Topics[0].Lessons.Add(
            CreateLesson(missingModule.Topics[0].Id, "Modulo 02/Aula 02.mp4", 1));
        existing.Modules.Add(missingModule);

        var result = new CourseContentSyncPlanner().Plan(existing, CreateStandardDetection(), RootPath);

        Assert.Equal(CourseContentSyncChangeKind.Missing, result.Modules.Single(item => item.ExistingModuleId == missingModule.Id).ChangeKind);
        Assert.Equal(CourseContentSyncChangeKind.Missing, result.Topics.Single(item => item.ExistingModuleId == missingModule.Id).ChangeKind);
        Assert.Equal(CourseContentSyncChangeKind.Missing, result.Lessons.Single(item => item.ExistingTopicId == missingModule.Topics[0].Id).ChangeKind);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_RenamedLessonIsReportedAsNewAndMissing()
    {
        var existing = CreateStandardCourse();
        var detected = CreateStandardDetection();
        detected.Modules[0].Topics[0].Lessons[0] =
            CreateDetectedLesson("Modulo 01/Topico 01/Aula renomeada.mp4", 1);

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(1, result.NewLessonCount);
        Assert.Equal(1, result.MissingLessonCount);
        Assert.Equal(0, result.UnchangedLessonCount);
        Assert.DoesNotContain(result.Lessons, item => item.ChangeKind == CourseContentSyncChangeKind.Unchanged);
        AssertCounterInvariants(result);
    }

    [Theory]
    [InlineData("module")]
    [InlineData("topic")]
    [InlineData("lesson")]
    public void Plan_DuplicatePersistedKeyReturnsAmbiguousStructure(string level)
    {
        var existing = CreateStandardCourse();
        var module = existing.Modules.Single();
        var topic = module.Topics.Single();

        if (level == "module")
        {
            existing.Modules.Add(CreateModule(existing.Id, "Modulo 01", 2));
        }
        else if (level == "topic")
        {
            module.Topics.Add(CreateTopic(module.Id, "Modulo 01/Topico 01", 2));
        }
        else
        {
            topic.Lessons.Add(CreateLesson(topic.Id, "Modulo 01/Topico 01/Aula 01.mp4", 2));
        }

        var result = new CourseContentSyncPlanner().Plan(existing, CreateStandardDetection(), RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.AmbiguousStructure, result.Status);
        Assert.False(result.Success);
        Assert.False(result.CanApply);
        Assert.NotEmpty(result.Diagnostics);
        AssertCounterInvariants(result);
    }

    [Theory]
    [InlineData("module")]
    [InlineData("topic")]
    [InlineData("lesson")]
    public void Plan_DuplicateDetectedKeyAfterNormalizationReturnsAmbiguousStructure(string level)
    {
        var detected = CreateStandardDetection();
        var module = detected.Modules.Single();
        var topic = module.Topics.Single();

        if (level == "module")
        {
            detected.Modules.Add(CreateDetectedModule(
                "Modulo 01//",
                2,
                CreateDetectedTopic(
                    "Topico 02",
                    1,
                    CreateDetectedLesson("Modulo 01/Topico 02/Aula 02.mp4", 1))));
        }
        else if (level == "topic")
        {
            module.Topics.Add(CreateDetectedTopic(
                "Topico 01//",
                2,
                CreateDetectedLesson("Modulo 01/Topico 01/Aula 02.mp4", 1)));
        }
        else
        {
            topic.Lessons.Add(CreateDetectedLesson("Modulo 01/Topico 01/Aula 01.mp4", 2));
        }

        var result = new CourseContentSyncPlanner().Plan(CreateStandardCourse(), detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.AmbiguousStructure, result.Status);
        Assert.False(result.CanApply);
        Assert.NotEmpty(result.Diagnostics);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_UnsafePersistedReferencesWithoutFallbackReturnInsufficientReferenceData()
    {
        var existing = CreateStandardCourse();
        var module = existing.Modules.Single();
        var topic = module.Topics.Single();
        var lesson = topic.Lessons.Single();
        module.SourceRelativePath = "../Modulo";
        topic.SourceRelativePath = string.Empty;
        lesson.RelativeFilePath = string.Empty;
        lesson.LocalFilePath = string.Empty;
        lesson.FilePath = string.Empty;

        var result = new CourseContentSyncPlanner().Plan(existing, CreateStandardDetection(), RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.InsufficientReferenceData, result.Status);
        Assert.False(result.CanApply);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(result.Modules);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_UsesConservativeLessonTopicAndModuleFallbacksWithoutPersistingThem()
    {
        var existing = CreateDirectCourse();
        var module = existing.Modules.Single();
        var topic = module.Topics.Single();
        var lesson = topic.Lessons.Single();
        module.SourceRelativePath = string.Empty;
        topic.SourceRelativePath = string.Empty;
        lesson.RelativeFilePath = string.Empty;
        lesson.LocalFilePath = Path.Combine(RootPath, "Modulo 01", "Aula 01.mp4");
        var detected = CreateDirectDetection();

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, result.Status);
        Assert.Equal("Modulo 01", result.Modules.Single().SourceRelativePath);
        Assert.Equal("Modulo 01", result.Topics.Single().SourceRelativePath);
        Assert.Equal("Modulo 01/Aula 01.mp4", result.Lessons.Single().RelativeFilePath);
        Assert.Equal(string.Empty, module.SourceRelativePath);
        Assert.Equal(string.Empty, topic.SourceRelativePath);
        Assert.Equal(string.Empty, lesson.RelativeFilePath);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_SingleNestedLessonCannotInventModulePath()
    {
        var existing = CreateStandardCourse();
        existing.Modules.Single().SourceRelativePath = string.Empty;

        var result = new CourseContentSyncPlanner().Plan(existing, CreateStandardDetection(), RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.InsufficientReferenceData, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("módulo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plan_IsCaseInsensitiveOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var existing = CreateStandardCourse();
        var detected = CreateStandardDetection();
        detected.Modules[0].RelativePath = "modulo 01";
        detected.Modules[0].Topics[0].RelativePath = "topico 01";
        detected.Modules[0].Topics[0].Lessons[0].RelativePath =
            "modulo 01/topico 01/aula 01.MP4";

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, result.Status);
        Assert.Equal(existing.Modules[0].Id, result.Modules.Single().ExistingModuleId);
        Assert.Equal(existing.Modules[0].Topics[0].Id, result.Topics.Single().ExistingTopicId);
        Assert.Equal(existing.Modules[0].Topics[0].Lessons[0].Id, result.Lessons.Single().ExistingLessonId);
    }

    [Fact]
    public void Plan_SupportsStructuralRootIdentity()
    {
        var course = new CourseRecord { Id = Guid.NewGuid(), Title = "Curso na raiz" };
        var module = CreateModule(course.Id, ".", 1);
        var topic = CreateTopic(module.Id, ".", 1);
        topic.Lessons.Add(CreateLesson(topic.Id, "Aula 01.mp4", 1));
        module.Topics.Add(topic);
        course.Modules.Add(module);
        var detected = CreateDetection(CreateDetectedModule(
            ".",
            1,
            CreateDetectedTopic(
                ".",
                1,
                CreateDetectedLesson("Aula 01.mp4", 1))));

        var result = new CourseContentSyncPlanner().Plan(course, detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, result.Status);
        Assert.Equal(".", result.Modules.Single().SourceRelativePath);
        Assert.Equal(".", result.Topics.Single().SourceRelativePath);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_MixedChangesKeepsEveryCounterInvariant()
    {
        var existing = CreateStandardCourse();
        var topic = existing.Modules[0].Topics[0];
        topic.Lessons.Add(CreateLesson(topic.Id, "Modulo 01/Topico 01/Aula ausente.mp4", 2));
        var detected = CreateStandardDetection();
        detected.Modules[0].Topics[0].Lessons.Add(
            CreateDetectedLesson("Modulo 01/Topico 01/Aula nova.mp4", 2));

        var result = new CourseContentSyncPlanner().Plan(existing, detected, RootPath);

        Assert.True(result.CanApply);
        Assert.Equal(1, result.UnchangedLessonCount);
        Assert.Equal(1, result.NewLessonCount);
        Assert.Equal(1, result.MissingLessonCount);
        AssertCounterInvariants(result);
    }

    [Fact]
    public void Plan_IsDeterministicForEquivalentInputsWithDifferentEnumerationOrder()
    {
        var existing = CreateTwoModuleCourse();
        var detected = CreateTwoModuleDetection();
        var planner = new CourseContentSyncPlanner();
        var first = planner.Plan(existing, detected, RootPath);

        existing.Modules.Reverse();
        foreach (var module in existing.Modules)
        {
            module.Topics.Reverse();
            foreach (var topic in module.Topics)
            {
                topic.Lessons.Reverse();
            }
        }

        detected.Modules.Reverse();
        foreach (var module in detected.Modules)
        {
            module.Topics.Reverse();
            foreach (var topic in module.Topics)
            {
                topic.Lessons.Reverse();
            }
        }

        var second = planner.Plan(existing, detected, RootPath);

        Assert.Equal(SemanticItems(first), SemanticItems(second));
        AssertCounterInvariants(first);
        AssertCounterInvariants(second);
    }

    [Fact]
    public void Plan_NoVideosDoesNotClassifyPersistedContentAsMissing()
    {
        var detected = CreateStandardDetection();
        detected.Modules[0].Topics[0].Lessons.Clear();

        var result = new CourseContentSyncPlanner().Plan(CreateStandardCourse(), detected, RootPath);

        Assert.Equal(CourseContentSyncPreviewStatus.NoVideosFound, result.Status);
        Assert.Empty(result.Modules);
        Assert.Empty(result.Topics);
        Assert.Empty(result.Lessons);
        AssertCounterInvariants(result);
    }

    private static CourseRecord CreateStandardCourse()
    {
        var course = new CourseRecord
        {
            Id = Guid.NewGuid(),
            Title = "Curso persistido"
        };
        var module = CreateModule(course.Id, "Modulo 01", 1);
        var topic = CreateTopic(module.Id, "Modulo 01/Topico 01", 1);
        topic.Lessons.Add(CreateLesson(topic.Id, "Modulo 01/Topico 01/Aula 01.mp4", 1));
        module.Topics.Add(topic);
        course.Modules.Add(module);
        return course;
    }

    private static DetectedCourseStructure CreateStandardDetection()
        => CreateDetection(CreateDetectedModule(
            "Modulo 01",
            1,
            CreateDetectedTopic(
                "Topico 01",
                1,
                CreateDetectedLesson("Modulo 01/Topico 01/Aula 01.mp4", 1))));

    private static CourseRecord CreateDirectCourse()
    {
        var course = new CourseRecord { Id = Guid.NewGuid(), Title = "Curso direto" };
        var module = CreateModule(course.Id, "Modulo 01", 1);
        var topic = CreateTopic(module.Id, "Modulo 01", 1);
        topic.Lessons.Add(CreateLesson(topic.Id, "Modulo 01/Aula 01.mp4", 1));
        module.Topics.Add(topic);
        course.Modules.Add(module);
        return course;
    }

    private static DetectedCourseStructure CreateDirectDetection()
        => CreateDetection(CreateDetectedModule(
            "Modulo 01",
            1,
            CreateDetectedTopic(
                ".",
                1,
                CreateDetectedLesson("Modulo 01/Aula 01.mp4", 1))));

    private static CourseRecord CreateTwoModuleCourse()
    {
        var course = CreateStandardCourse();
        var module = CreateModule(course.Id, "Modulo 02", 2);
        var topic = CreateTopic(module.Id, "Modulo 02", 1);
        topic.Lessons.Add(CreateLesson(topic.Id, "Modulo 02/Aula 02.mp4", 1));
        module.Topics.Add(topic);
        course.Modules.Add(module);
        return course;
    }

    private static DetectedCourseStructure CreateTwoModuleDetection()
    {
        var detected = CreateStandardDetection();
        detected.Modules.Add(CreateDetectedModule(
            "Modulo 02",
            2,
            CreateDetectedTopic(
                ".",
                1,
                CreateDetectedLesson("Modulo 02/Aula 02.mp4", 1))));
        return detected;
    }

    private static CourseRecord CreateCourse(params ModuleRecord[] modules)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = "Curso persistido",
            Modules = [.. modules]
        };

    private static ModuleRecord CreateModule(
        Guid courseId,
        string sourceRelativePath,
        int order,
        params TopicRecord[] topics)
    {
        var module = new ModuleRecord
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Title = $"Módulo {order}",
            SourceRelativePath = sourceRelativePath,
            Order = order,
            Topics = [.. topics]
        };
        foreach (var topic in module.Topics)
        {
            topic.ModuleId = module.Id;
        }

        return module;
    }

    private static TopicRecord CreateTopic(
        Guid moduleId,
        string sourceRelativePath,
        int order,
        params LessonRecord[] lessons)
    {
        var topic = new TopicRecord
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            Title = $"Tópico {order}",
            SourceRelativePath = sourceRelativePath,
            Order = order,
            Lessons = [.. lessons]
        };
        foreach (var lesson in topic.Lessons)
        {
            lesson.TopicId = topic.Id;
        }

        return topic;
    }

    private static LessonRecord CreateLesson(Guid topicId, string relativeFilePath, int order)
        => new()
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            Title = "Aula persistida",
            RelativeFilePath = relativeFilePath,
            SourceType = LessonSourceType.LocalFile,
            Order = order
        };

    private static DetectedCourseStructure CreateDetection(params DetectedModuleStructure[] modules)
        => new()
        {
            CourseId = Guid.NewGuid(),
            RootFolderName = "Curso detectado",
            RootFolderPath = RootPath,
            ScannedAt = ScannedAtUtc,
            Modules = [.. modules]
        };

    private static DetectedModuleStructure CreateDetectedModule(
        string relativePath,
        int order,
        params DetectedTopicStructure[] topics)
        => new()
        {
            ModuleId = Guid.NewGuid(),
            RawName = relativePath,
            RelativePath = relativePath,
            Order = order,
            Topics = [.. topics]
        };

    private static DetectedTopicStructure CreateDetectedTopic(
        string relativePath,
        int order,
        params DetectedLessonFile[] lessons)
        => new()
        {
            TopicId = Guid.NewGuid(),
            RawName = relativePath,
            RelativePath = relativePath,
            Order = order,
            Lessons = [.. lessons]
        };

    private static DetectedLessonFile CreateDetectedLesson(string relativePath, int order)
        => new()
        {
            LessonId = Guid.NewGuid(),
            RawName = Path.GetFileNameWithoutExtension(relativePath),
            FileName = Path.GetFileName(relativePath),
            RelativePath = relativePath,
            AbsolutePath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            Duration = TimeSpan.FromMinutes(10),
            FileSizeBytes = 1024,
            Order = order
        };

    private static void AssertCounterInvariants(CourseContentSyncPreviewResult result)
    {
        Assert.Equal(result.ExistingModuleCount, result.UnchangedModuleCount + result.MissingModuleCount);
        Assert.Equal(result.CandidateModuleCount, result.UnchangedModuleCount + result.NewModuleCount);
        Assert.Equal(result.ExistingTopicCount, result.UnchangedTopicCount + result.MissingTopicCount);
        Assert.Equal(result.CandidateTopicCount, result.UnchangedTopicCount + result.NewTopicCount);
        Assert.Equal(result.ExistingLessonCount, result.UnchangedLessonCount + result.MissingLessonCount);
        Assert.Equal(result.CandidateLessonCount, result.UnchangedLessonCount + result.NewLessonCount);
    }

    private static string[] SemanticItems(CourseContentSyncPreviewResult result)
        =>
        [
            .. result.Modules.Select(item =>
                $"M|{item.SourceRelativePath}|{item.ExistingOrder}|{item.DetectedOrder}|{item.ChangeKind}"),
            .. result.Topics.Select(item =>
                $"T|{item.ModuleSourceRelativePath}|{item.SourceRelativePath}|{item.ExistingOrder}|{item.DetectedOrder}|{item.ChangeKind}"),
            .. result.Lessons.Select(item =>
                $"L|{item.ModuleSourceRelativePath}|{item.TopicSourceRelativePath}|{item.RelativeFilePath}|{item.ExistingOrder}|{item.DetectedOrder}|{item.ChangeKind}")
        ];
}
