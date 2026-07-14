using studyhub.application.Contracts.CourseContentSync;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class CourseContentSyncApplierTests
{
    [Fact]
    public void Apply_NewDirectTopic_UsesParentModulePresentationAndCreatesPermanentTree()
    {
        var course = new CourseRecord { Id = Guid.NewGuid() };
        var plan = new CourseContentSyncPreviewResult
        {
            CourseId = course.Id,
            Status = CourseContentSyncPreviewStatus.Ready,
            Modules =
            [
                new CourseContentSyncModulePlanItem
                {
                    SourceRelativePath = "Module 03",
                    DetectedName = "03 - Foundations",
                    DetectedOrder = 3,
                    ChangeKind = CourseContentSyncChangeKind.New
                }
            ],
            Topics =
            [
                new CourseContentSyncTopicPlanItem
                {
                    ModuleSourceRelativePath = "Module 03",
                    SourceRelativePath = "Module 03",
                    DetectedName = ".",
                    DetectedOrder = 1,
                    ChangeKind = CourseContentSyncChangeKind.New
                }
            ],
            Lessons =
            [
                new CourseContentSyncLessonPlanItem
                {
                    ModuleSourceRelativePath = "Module 03",
                    TopicSourceRelativePath = "Module 03",
                    RelativeFilePath = "Module 03/01 - Introduction.mp4",
                    DetectedName = "01 - Introduction.mp4",
                    DetectedOrder = 1,
                    DetectedDuration = TimeSpan.FromMinutes(8),
                    ChangeKind = CourseContentSyncChangeKind.New
                }
            ]
        };

        var summary = new CourseContentSyncApplier().Apply(
            course,
            plan,
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "studyhub-direct-topic")));

        var module = Assert.Single(course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);
        Assert.NotEqual(Guid.Empty, module.Id);
        Assert.NotEqual(Guid.Empty, topic.Id);
        Assert.NotEqual(Guid.Empty, lesson.Id);
        Assert.Equal(course.Id, module.CourseId);
        Assert.Equal(module.Id, topic.ModuleId);
        Assert.Equal(topic.Id, lesson.TopicId);
        Assert.Equal(module.RawTitle, topic.RawTitle);
        Assert.Equal(module.Title, topic.Title);
        Assert.Equal(1, summary.CreatedModuleCount);
        Assert.Equal(1, summary.CreatedTopicCount);
        Assert.Equal(1, summary.CreatedLessonCount);
    }

    [Fact]
    public void Apply_MissingTree_CanonicalizesReferencesWithoutDeletingHistoricalState()
    {
        var completedAt = new DateTime(2026, 5, 5, 10, 0, 0, DateTimeKind.Utc);
        var course = CreateExistingCourse(completedAt, isAvailable: true);
        var module = Assert.Single(course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);
        module.SourceRelativePath = @"Module 01\";
        topic.SourceRelativePath = @"Module 01\Topic 01";
        lesson.RelativeFilePath = @"Module 01\Topic 01\Lesson.mp4";
        var plan = CreateExistingPlan(course, CourseContentSyncChangeKind.Missing, TimeSpan.Zero);

        var summary = new CourseContentSyncApplier().Apply(
            course,
            plan,
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "studyhub-missing-tree")));

        Assert.Single(course.Modules);
        Assert.Single(module.Topics);
        Assert.Single(topic.Lessons);
        Assert.False(module.IsAvailable);
        Assert.False(topic.IsAvailable);
        Assert.False(lesson.IsAvailable);
        Assert.Equal("Module 01", module.SourceRelativePath);
        Assert.Equal("Module 01/Topic 01", topic.SourceRelativePath);
        Assert.Equal("Module 01/Topic 01/Lesson.mp4", lesson.RelativeFilePath);
        Assert.Equal(completedAt, topic.CompletedAtUtc);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(42.5, lesson.WatchedPercentage);
        Assert.Equal(123, lesson.LastPlaybackPositionSeconds);
        Assert.Equal(1, summary.MarkedMissingModuleCount);
        Assert.Equal(1, summary.MarkedMissingTopicCount);
        Assert.Equal(1, summary.MarkedMissingLessonCount);
    }

    [Fact]
    public void Apply_ReappearingTree_RestoresAvailabilityAndPreservesUnknownDurationAndProgress()
    {
        var completedAt = new DateTime(2026, 5, 5, 10, 0, 0, DateTimeKind.Utc);
        var course = CreateExistingCourse(completedAt, isAvailable: false);
        var module = Assert.Single(course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);
        var plan = CreateExistingPlan(course, CourseContentSyncChangeKind.Unchanged, TimeSpan.Zero);

        var summary = new CourseContentSyncApplier().Apply(
            course,
            plan,
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "studyhub-restored-tree")));

        Assert.True(module.IsAvailable);
        Assert.True(topic.IsAvailable);
        Assert.True(lesson.IsAvailable);
        Assert.Equal(17, lesson.DurationMinutes);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(42.5, lesson.WatchedPercentage);
        Assert.Equal(123, lesson.LastPlaybackPositionSeconds);
        Assert.Equal(completedAt, topic.CompletedAtUtc);
        Assert.Equal(3, summary.RestoredAvailableItemCount);
    }

    private static CourseRecord CreateExistingCourse(DateTime completedAt, bool isAvailable)
    {
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        return new CourseRecord
        {
            Id = courseId,
            Modules =
            [
                new ModuleRecord
                {
                    Id = moduleId,
                    CourseId = courseId,
                    Order = 1,
                    SourceRelativePath = "Module 01",
                    IsAvailable = isAvailable,
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = topicId,
                            ModuleId = moduleId,
                            Order = 1,
                            SourceRelativePath = "Module 01/Topic 01",
                            CompletedAtUtc = completedAt,
                            IsAvailable = isAvailable,
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = Guid.NewGuid(),
                                    TopicId = topicId,
                                    Order = 1,
                                    RelativeFilePath = "Module 01/Topic 01/Lesson.mp4",
                                    DurationMinutes = 17,
                                    Status = LessonStatus.InProgress,
                                    WatchedPercentage = 42.5,
                                    LastPlaybackPositionSeconds = 123,
                                    IsAvailable = isAvailable
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static CourseContentSyncPreviewResult CreateExistingPlan(
        CourseRecord course,
        CourseContentSyncChangeKind changeKind,
        TimeSpan detectedDuration)
    {
        var module = Assert.Single(course.Modules);
        var topic = Assert.Single(module.Topics);
        var lesson = Assert.Single(topic.Lessons);
        return new CourseContentSyncPreviewResult
        {
            CourseId = course.Id,
            Status = changeKind == CourseContentSyncChangeKind.Unchanged
                ? CourseContentSyncPreviewStatus.NoChanges
                : CourseContentSyncPreviewStatus.Ready,
            Modules =
            [
                new CourseContentSyncModulePlanItem
                {
                    ExistingModuleId = module.Id,
                    SourceRelativePath = "Module 01",
                    ExistingOrder = module.Order,
                    ChangeKind = changeKind
                }
            ],
            Topics =
            [
                new CourseContentSyncTopicPlanItem
                {
                    ExistingModuleId = module.Id,
                    ExistingTopicId = topic.Id,
                    ModuleSourceRelativePath = "Module 01",
                    SourceRelativePath = "Module 01/Topic 01",
                    ExistingOrder = topic.Order,
                    ChangeKind = changeKind
                }
            ],
            Lessons =
            [
                new CourseContentSyncLessonPlanItem
                {
                    ExistingTopicId = topic.Id,
                    ExistingLessonId = lesson.Id,
                    ModuleSourceRelativePath = "Module 01",
                    TopicSourceRelativePath = "Module 01/Topic 01",
                    RelativeFilePath = "Module 01/Topic 01/Lesson.mp4",
                    ExistingOrder = lesson.Order,
                    DetectedDuration = detectedDuration,
                    ChangeKind = changeKind
                }
            ]
        };
    }
}
