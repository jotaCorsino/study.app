using studyhub.app.Components.Course;
using studyhub.domain.Entities;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class CourseAvailabilityPresentationHelperTests
{
    [Fact]
    public void Summarize_NoUnavailableContent_ReturnsEmptySummary()
    {
        var course = CreateCourse(
            new Module
            {
                IsAvailable = true,
                Topics =
                [
                    new Topic
                    {
                        IsAvailable = true,
                        Lessons = [new Lesson { IsAvailable = true }]
                    }
                ]
            });

        var summary = CourseAvailabilityPresentationHelper.Summarize(course);

        Assert.Equal(0, summary.UnavailableModuleCount);
        Assert.Equal(0, summary.UnavailableTopicCount);
        Assert.Equal(0, summary.UnavailableLessonCount);
        Assert.Equal(0, summary.TotalUnavailableCount);
        Assert.False(summary.HasUnavailableContent);
    }

    [Fact]
    public void Summarize_MixedUnavailableContent_ReturnsCountsPerLevel()
    {
        var course = CreateCourse(
            new Module
            {
                IsAvailable = false,
                Topics =
                [
                    new Topic
                    {
                        IsAvailable = false,
                        Lessons =
                        [
                            new Lesson { IsAvailable = false },
                            new Lesson { IsAvailable = false }
                        ]
                    },
                    new Topic
                    {
                        IsAvailable = false,
                        Lessons =
                        [
                            new Lesson { IsAvailable = false },
                            new Lesson { IsAvailable = true }
                        ]
                    }
                ]
            });

        var summary = CourseAvailabilityPresentationHelper.Summarize(course);

        Assert.Equal(1, summary.UnavailableModuleCount);
        Assert.Equal(2, summary.UnavailableTopicCount);
        Assert.Equal(3, summary.UnavailableLessonCount);
        Assert.Equal(6, summary.TotalUnavailableCount);
        Assert.True(summary.HasUnavailableContent);
    }

    [Fact]
    public void GetModuleState_UnavailableModule_ReturnsUnavailable()
    {
        var module = new Module
        {
            IsAvailable = false,
            Topics =
            [
                new Topic
                {
                    IsAvailable = true,
                    Lessons = [new Lesson { IsAvailable = true }]
                }
            ]
        };

        var state = CourseAvailabilityPresentationHelper.GetModuleState(module);

        Assert.Equal(ModuleAvailabilityPresentationState.Unavailable, state);
    }

    [Fact]
    public void GetModuleState_AvailableModuleWithUnavailableDescendant_ReturnsPartiallyUnavailable()
    {
        var module = new Module
        {
            IsAvailable = true,
            Topics =
            [
                new Topic
                {
                    IsAvailable = false,
                    Lessons =
                    [
                        new Lesson { IsAvailable = true },
                        new Lesson { IsAvailable = false }
                    ]
                }
            ]
        };

        var state = CourseAvailabilityPresentationHelper.GetModuleState(module);

        Assert.Equal(ModuleAvailabilityPresentationState.PartiallyUnavailable, state);
        Assert.Equal(2, CourseAvailabilityPresentationHelper.CountUnavailableDescendants(module));
    }

    [Fact]
    public void GetLessonPresentation_CompletedUnavailable_PreservesBothStates()
    {
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            Status = LessonStatus.Completed,
            IsAvailable = false
        };
        var statuses = new Dictionary<Guid, LessonStatus>
        {
            [lesson.Id] = LessonStatus.Completed
        };

        var presentation = CourseAvailabilityPresentationHelper.GetLessonPresentation(
            lesson,
            statuses);

        Assert.Equal(LessonStatus.Completed, presentation.ProgressStatus);
        Assert.False(presentation.IsAvailable);
        Assert.True(presentation.IsUnavailable);
    }

    [Fact]
    public void GetLessonPresentation_InProgressUnavailable_PreservesBothStates()
    {
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            Status = LessonStatus.InProgress,
            IsAvailable = false
        };
        var statuses = new Dictionary<Guid, LessonStatus>
        {
            [lesson.Id] = LessonStatus.InProgress
        };

        var presentation = CourseAvailabilityPresentationHelper.GetLessonPresentation(
            lesson,
            statuses);

        Assert.Equal(LessonStatus.InProgress, presentation.ProgressStatus);
        Assert.False(presentation.IsAvailable);
        Assert.True(presentation.IsUnavailable);
    }

    [Fact]
    public void HasHistoricallyCompletedWithPendingAvailableContent_CompletedTopicWithAvailablePendingLesson_ReturnsTrue()
    {
        var completedLesson = CreateLesson(LessonStatus.Completed, isAvailable: true);
        var pendingLesson = CreateLesson(LessonStatus.NotStarted, isAvailable: true);
        var topic = CreateCompletedTopic(completedLesson, pendingLesson);
        var statuses = CreateStatuses(completedLesson, pendingLesson);

        var result = CourseAvailabilityPresentationHelper
            .HasHistoricallyCompletedWithPendingAvailableContent(topic, statuses);

        Assert.True(result);
    }

    [Fact]
    public void HasHistoricallyCompletedWithPendingAvailableContent_AllLessonsCompleted_ReturnsFalse()
    {
        var firstLesson = CreateLesson(LessonStatus.Completed, isAvailable: true);
        var secondLesson = CreateLesson(LessonStatus.Completed, isAvailable: true);
        var topic = CreateCompletedTopic(firstLesson, secondLesson);
        var statuses = CreateStatuses(firstLesson, secondLesson);

        var result = CourseAvailabilityPresentationHelper
            .HasHistoricallyCompletedWithPendingAvailableContent(topic, statuses);

        Assert.False(result);
    }

    [Fact]
    public void HasHistoricallyCompletedWithPendingAvailableContent_TopicWithoutCompletedAt_ReturnsFalse()
    {
        var pendingLesson = CreateLesson(LessonStatus.NotStarted, isAvailable: true);
        var topic = new Topic
        {
            CompletedAtUtc = null,
            Lessons = [pendingLesson]
        };

        var result = CourseAvailabilityPresentationHelper
            .HasHistoricallyCompletedWithPendingAvailableContent(
                topic,
                CreateStatuses(pendingLesson));

        Assert.False(result);
    }

    [Fact]
    public void HasHistoricallyCompletedWithPendingAvailableContent_OnlyUnavailablePendingLesson_ReturnsFalse()
    {
        var completedLesson = CreateLesson(LessonStatus.Completed, isAvailable: true);
        var unavailablePendingLesson = CreateLesson(LessonStatus.NotStarted, isAvailable: false);
        var topic = CreateCompletedTopic(completedLesson, unavailablePendingLesson);
        var statuses = CreateStatuses(completedLesson, unavailablePendingLesson);

        var result = CourseAvailabilityPresentationHelper
            .HasHistoricallyCompletedWithPendingAvailableContent(topic, statuses);

        Assert.False(result);
    }

    [Fact]
    public void PresentationMethods_DoNotMutateDomainGraph()
    {
        var lesson = CreateLesson(LessonStatus.InProgress, isAvailable: false);
        lesson.WatchedPercentage = 47.5;
        lesson.LastPlaybackPosition = TimeSpan.FromMinutes(3);
        var completedAtUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            IsAvailable = false,
            CompletedAtUtc = completedAtUtc,
            Lessons = [lesson]
        };
        var module = new Module
        {
            Id = Guid.NewGuid(),
            IsAvailable = true,
            Topics = [topic]
        };
        var course = CreateCourse(module);
        var statuses = CreateStatuses(lesson);

        _ = CourseAvailabilityPresentationHelper.Summarize(course);
        _ = CourseAvailabilityPresentationHelper.GetModuleState(module);
        _ = CourseAvailabilityPresentationHelper.CountUnavailableDescendants(module);
        _ = CourseAvailabilityPresentationHelper.GetLessonPresentation(lesson, statuses);
        _ = CourseAvailabilityPresentationHelper
            .HasHistoricallyCompletedWithPendingAvailableContent(topic, statuses);

        Assert.Same(module, Assert.Single(course.Modules));
        Assert.Same(topic, Assert.Single(module.Topics));
        Assert.Same(lesson, Assert.Single(topic.Lessons));
        Assert.True(module.IsAvailable);
        Assert.False(topic.IsAvailable);
        Assert.Equal(completedAtUtc, topic.CompletedAtUtc);
        Assert.False(lesson.IsAvailable);
        Assert.Equal(LessonStatus.InProgress, lesson.Status);
        Assert.Equal(47.5, lesson.WatchedPercentage);
        Assert.Equal(TimeSpan.FromMinutes(3), lesson.LastPlaybackPosition);
    }

    private static Course CreateCourse(params Module[] modules)
        => new()
        {
            Id = Guid.NewGuid(),
            Modules = [.. modules]
        };

    private static Topic CreateCompletedTopic(params Lesson[] lessons)
        => new()
        {
            CompletedAtUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            Lessons = [.. lessons]
        };

    private static Lesson CreateLesson(LessonStatus status, bool isAvailable)
        => new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            IsAvailable = isAvailable
        };

    private static Dictionary<Guid, LessonStatus> CreateStatuses(params Lesson[] lessons)
        => lessons.ToDictionary(lesson => lesson.Id, lesson => lesson.Status);
}
