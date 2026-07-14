using studyhub.domain.Entities;
using studyhub.shared.Enums;
using CourseEntity = studyhub.domain.Entities.Course;

namespace studyhub.app.Components.Course;

public enum ModuleAvailabilityPresentationState
{
    Available,
    PartiallyUnavailable,
    Unavailable
}

public readonly record struct CourseAvailabilitySummary(
    int UnavailableModuleCount,
    int UnavailableTopicCount,
    int UnavailableLessonCount)
{
    public int TotalUnavailableCount =>
        UnavailableModuleCount + UnavailableTopicCount + UnavailableLessonCount;

    public bool HasUnavailableContent => TotalUnavailableCount > 0;
}

public readonly record struct LessonAvailabilityPresentation(
    LessonStatus ProgressStatus,
    bool IsAvailable)
{
    public bool IsUnavailable => !IsAvailable;
}

public static class CourseAvailabilityPresentationHelper
{
    public static CourseAvailabilitySummary Summarize(CourseEntity course)
    {
        ArgumentNullException.ThrowIfNull(course);

        return new CourseAvailabilitySummary(
            course.Modules.Count(module => !module.IsAvailable),
            course.Modules.Sum(module => module.Topics.Count(topic => !topic.IsAvailable)),
            course.Modules.Sum(module =>
                module.Topics.Sum(topic => topic.Lessons.Count(lesson => !lesson.IsAvailable))));
    }

    public static ModuleAvailabilityPresentationState GetModuleState(Module module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (!module.IsAvailable)
        {
            return ModuleAvailabilityPresentationState.Unavailable;
        }

        return CountUnavailableDescendants(module) > 0
            ? ModuleAvailabilityPresentationState.PartiallyUnavailable
            : ModuleAvailabilityPresentationState.Available;
    }

    public static int CountUnavailableDescendants(Module module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var unavailableTopicCount = module.Topics.Count(topic => !topic.IsAvailable);
        var unavailableLessonCount = module.Topics.Sum(
            topic => topic.Lessons.Count(lesson => !lesson.IsAvailable));

        return unavailableTopicCount + unavailableLessonCount;
    }

    public static LessonAvailabilityPresentation GetLessonPresentation(
        Lesson lesson,
        IReadOnlyDictionary<Guid, LessonStatus> lessonStatuses)
    {
        ArgumentNullException.ThrowIfNull(lesson);
        ArgumentNullException.ThrowIfNull(lessonStatuses);

        return new LessonAvailabilityPresentation(
            ResolveLessonStatus(lesson, lessonStatuses),
            lesson.IsAvailable);
    }

    public static bool HasHistoricallyCompletedWithPendingAvailableContent(
        Topic topic,
        IReadOnlyDictionary<Guid, LessonStatus> lessonStatuses)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(lessonStatuses);

        return topic.CompletedAtUtc.HasValue &&
               topic.Lessons.Any(lesson =>
                   lesson.IsAvailable &&
                   ResolveLessonStatus(lesson, lessonStatuses) != LessonStatus.Completed);
    }

    private static LessonStatus ResolveLessonStatus(
        Lesson lesson,
        IReadOnlyDictionary<Guid, LessonStatus> lessonStatuses)
        => lessonStatuses.TryGetValue(lesson.Id, out var status)
            ? status
            : lesson.Status;
}
