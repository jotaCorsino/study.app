using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Interfaces;
using studyhub.app.services;
using studyhub.domain.Entities;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class ExternalLessonPlaybackServiceTests
{
    [Fact]
    public async Task ActivateAsync_PreservesInitialStartOffset_ForExternalSession()
    {
        var service = CreateService();
        var lesson = CreateExternalLesson();

        var sessionToken = await service.ActivateAsync(
            Guid.NewGuid(),
            lesson,
            playbackSpeed: 1.0,
            initialStartOffset: TimeSpan.FromSeconds(10));

        var snapshot = service.Snapshot;

        Assert.Equal(sessionToken, snapshot.SessionToken);
        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.InitialStartOffset);
        Assert.Equal(ExternalLessonPlaybackStatus.Pending, snapshot.Status);
    }

    [Fact]
    public async Task ActivateAsync_UsesZeroInitialStartOffset_WhenValueIsNegative()
    {
        var service = CreateService();
        var lesson = CreateExternalLesson();

        await service.ActivateAsync(
            Guid.NewGuid(),
            lesson,
            playbackSpeed: 1.0,
            initialStartOffset: TimeSpan.FromSeconds(-4));

        Assert.Equal(TimeSpan.Zero, service.Snapshot.InitialStartOffset);
    }

    [Fact]
    public async Task SetPlaybackSpeedAsync_DoesNotChangeInitialStartOffset()
    {
        var service = CreateService();
        var lesson = CreateExternalLesson();

        var sessionToken = await service.ActivateAsync(
            Guid.NewGuid(),
            lesson,
            playbackSpeed: 1.0,
            initialStartOffset: TimeSpan.FromSeconds(12));

        await service.SetPlaybackSpeedAsync(sessionToken, 1.5);

        Assert.Equal(TimeSpan.FromSeconds(12), service.Snapshot.InitialStartOffset);
    }

    private static ExternalLessonPlaybackService CreateService()
    {
        return new ExternalLessonPlaybackService(
            new StubProgressService(),
            new StubCourseGenerationHistoryService(),
            new StubExternalLessonRuntimeStateService(),
            NullLogger<ExternalLessonPlaybackService>.Instance);
    }

    private static Lesson CreateExternalLesson()
    {
        return new Lesson
        {
            Id = Guid.NewGuid(),
            SourceType = LessonSourceType.ExternalVideo,
            ExternalUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Provider = "YouTube",
            Duration = TimeSpan.FromMinutes(5)
        };
    }

    private sealed class StubProgressService : IProgressService
    {
        public Task<Progress?> GetProgressByCourseAsync(Guid courseId)
            => Task.FromResult<Progress?>(null);

        public Task<List<Progress>> GetAllProgressAsync()
            => Task.FromResult(new List<Progress>());

        public Task OpenLessonAsync(Guid courseId, Guid lessonId)
            => Task.CompletedTask;

        public Task MarkLessonCompletedAsync(Guid courseId, Guid lessonId)
            => Task.CompletedTask;

        public Task UpdateLessonProgressAsync(Guid courseId, Guid lessonId, double watchedPercentage)
            => Task.CompletedTask;

        public Task UpdateLessonPlaybackAsync(Guid courseId, Guid lessonId, TimeSpan currentPosition, TimeSpan totalDuration, bool markAsCompleted = false)
            => Task.CompletedTask;
    }

    private sealed class StubCourseGenerationHistoryService : ICourseGenerationHistoryService
    {
        public Task RecordStepAsync(CourseGenerationStepEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, CourseGenerationStepEntry>> GetStepStatesAsync(Guid courseId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, CourseGenerationStepEntry>>(new Dictionary<string, CourseGenerationStepEntry>());
    }

    private sealed class StubExternalLessonRuntimeStateService : IExternalLessonRuntimeStateService
    {
        public Task RecordOpenedAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordReadyAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordFailureAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, string errorCode, string errorMessage, bool fallbackLaunched, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ExternalLessonRuntimeState?> GetStateAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken = default)
            => Task.FromResult<ExternalLessonRuntimeState?>(null);

        public Task<IReadOnlyList<ExternalLessonRuntimeState>> GetCourseStatesAsync(Guid courseId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExternalLessonRuntimeState>>(new List<ExternalLessonRuntimeState>());
    }
}
