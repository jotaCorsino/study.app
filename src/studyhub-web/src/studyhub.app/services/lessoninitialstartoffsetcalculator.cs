using studyhub.domain.Entities;
using studyhub.shared.Enums;

namespace studyhub.app.services;

public static class LessonInitialStartOffsetCalculator
{
    public static TimeSpan ResolveForLesson(
        Lesson? lesson,
        LessonSourceType sourceType,
        bool introSkipEnabled,
        int introSkipSeconds)
    {
        if (lesson == null || lesson.SourceType != sourceType)
        {
            return TimeSpan.Zero;
        }

        return ResolveOffsetWithPrecedence(lesson.LastPlaybackPosition, introSkipEnabled, introSkipSeconds);
    }

    public static TimeSpan ResolveForLesson(
        Lesson? lesson,
        bool introSkipEnabled,
        int introSkipSeconds)
    {
        return ResolveForLesson(lesson, LessonSourceType.LocalFile, introSkipEnabled, introSkipSeconds);
    }

    private static TimeSpan ResolveOffsetWithPrecedence(
        TimeSpan resumePosition,
        bool introSkipEnabled,
        int introSkipSeconds)
    {
        var normalizedResumePosition = NormalizeOffset(resumePosition);
        if (normalizedResumePosition > TimeSpan.Zero)
        {
            return normalizedResumePosition;
        }

        if (!introSkipEnabled || introSkipSeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(introSkipSeconds);
    }

    private static TimeSpan NormalizeOffset(TimeSpan offset)
    {
        return offset < TimeSpan.Zero ? TimeSpan.Zero : offset;
    }
}
