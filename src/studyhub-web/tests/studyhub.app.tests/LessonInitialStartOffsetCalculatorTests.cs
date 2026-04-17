using studyhub.app.services;
using studyhub.domain.Entities;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class LessonInitialStartOffsetCalculatorTests
{
    [Fact]
    public void ResolveForLesson_ReturnsResumePosition_WhenLessonHasSavedPlaybackPosition()
    {
        var lesson = CreateLesson(LessonSourceType.LocalFile, TimeSpan.FromSeconds(37));

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            introSkipEnabled: true,
            introSkipSeconds: 12);

        Assert.Equal(TimeSpan.FromSeconds(37), offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsIntroSkip_WhenLessonStartsFromZeroAndFeatureIsEnabled()
    {
        var lesson = CreateLesson(LessonSourceType.LocalFile, TimeSpan.Zero);

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            introSkipEnabled: true,
            introSkipSeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(10), offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsZero_WhenIntroSkipIsDisabled()
    {
        var lesson = CreateLesson(LessonSourceType.LocalFile, TimeSpan.Zero);

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            introSkipEnabled: false,
            introSkipSeconds: 15);

        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsZero_WhenLessonIsExternal()
    {
        var lesson = CreateLesson(LessonSourceType.ExternalVideo, TimeSpan.Zero);

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            introSkipEnabled: true,
            introSkipSeconds: 15);

        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsZero_WhenIntroSkipSecondsIsInvalid()
    {
        var lesson = CreateLesson(LessonSourceType.LocalFile, TimeSpan.Zero);

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            introSkipEnabled: true,
            introSkipSeconds: -3);

        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsResumePosition_ForExternalWhenResumeExists()
    {
        var lesson = CreateLesson(LessonSourceType.ExternalVideo, TimeSpan.FromSeconds(84));

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            LessonSourceType.ExternalVideo,
            introSkipEnabled: true,
            introSkipSeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(84), offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsIntroSkip_ForExternalWhenStartingFromZero()
    {
        var lesson = CreateLesson(LessonSourceType.ExternalVideo, TimeSpan.Zero);

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            LessonSourceType.ExternalVideo,
            introSkipEnabled: true,
            introSkipSeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(10), offset);
    }

    [Fact]
    public void ResolveForLesson_ReturnsZero_ForExternalWhenRequestedSourceDoesNotMatch()
    {
        var lesson = CreateLesson(LessonSourceType.ExternalVideo, TimeSpan.Zero);

        var offset = LessonInitialStartOffsetCalculator.ResolveForLesson(
            lesson,
            LessonSourceType.LocalFile,
            introSkipEnabled: true,
            introSkipSeconds: 10);

        Assert.Equal(TimeSpan.Zero, offset);
    }

    private static Lesson CreateLesson(LessonSourceType sourceType, TimeSpan lastPlaybackPosition)
    {
        return new Lesson
        {
            Id = Guid.NewGuid(),
            SourceType = sourceType,
            LastPlaybackPosition = lastPlaybackPosition
        };
    }
}
