using Microsoft.EntityFrameworkCore;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Interfaces;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public sealed class PersistedExternalLessonRuntimeStateService(IDbContextFactory<StudyHubDbContext> contextFactory) : IExternalLessonRuntimeStateService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;

    public Task RecordOpenedAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, CancellationToken cancellationToken = default)
    {
        return UpsertAsync(
            courseId,
            lessonId,
            provider,
            externalUrl,
            record =>
            {
                record.Status = "Opened";
                record.FallbackLaunched = false;
                record.LastOpenedAt = DateTime.UtcNow;
                record.UpdatedAt = DateTime.UtcNow;
            },
            cancellationToken);
    }

    public Task RecordReadyAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, CancellationToken cancellationToken = default)
    {
        return UpsertAsync(
            courseId,
            lessonId,
            provider,
            externalUrl,
            record =>
            {
                record.Status = "Ready";
                record.FallbackLaunched = false;
                record.LastSucceededAt = DateTime.UtcNow;
                record.UpdatedAt = DateTime.UtcNow;
            },
            cancellationToken);
    }

    public Task RecordFailureAsync(Guid courseId, Guid lessonId, string provider, string externalUrl, string errorCode, string errorMessage, bool fallbackLaunched, CancellationToken cancellationToken = default)
    {
        return UpsertAsync(
            courseId,
            lessonId,
            provider,
            externalUrl,
            record =>
            {
                record.Status = "Failed";
                record.LastErrorCode = errorCode ?? string.Empty;
                record.LastErrorMessage = errorMessage ?? string.Empty;
                record.FallbackLaunched = fallbackLaunched;
                record.LastFailedAt = DateTime.UtcNow;
                record.UpdatedAt = DateTime.UtcNow;
            },
            cancellationToken);
    }

    public async Task<ExternalLessonRuntimeState?> GetStateAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.ExternalLessonRuntimeStates
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CourseId == courseId && item.LessonId == lessonId, cancellationToken);

        return record == null ? null : ToContract(record);
    }

    public async Task<IReadOnlyList<ExternalLessonRuntimeState>> GetCourseStatesAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.ExternalLessonRuntimeStates
            .AsNoTracking()
            .Where(item => item.CourseId == courseId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(ToContract).ToList();
    }

    private async Task UpsertAsync(
        Guid courseId,
        Guid lessonId,
        string provider,
        string externalUrl,
        Action<ExternalLessonRuntimeStateRecord> updateRecord,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.ExternalLessonRuntimeStates
            .FirstOrDefaultAsync(item => item.LessonId == lessonId, cancellationToken);

        if (record == null)
        {
            record = new ExternalLessonRuntimeStateRecord
            {
                LessonId = lessonId,
                CourseId = courseId
            };

            await context.ExternalLessonRuntimeStates.AddAsync(record, cancellationToken);
        }

        record.CourseId = courseId;
        record.Provider = provider ?? string.Empty;
        record.ExternalUrl = externalUrl ?? string.Empty;
        updateRecord(record);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static ExternalLessonRuntimeState ToContract(ExternalLessonRuntimeStateRecord record)
    {
        return new ExternalLessonRuntimeState
        {
            CourseId = record.CourseId,
            LessonId = record.LessonId,
            Provider = record.Provider,
            Status = record.Status,
            ExternalUrl = record.ExternalUrl,
            LastErrorCode = record.LastErrorCode,
            LastErrorMessage = record.LastErrorMessage,
            FallbackLaunched = record.FallbackLaunched,
            LastOpenedAt = record.LastOpenedAt,
            LastSucceededAt = record.LastSucceededAt,
            LastFailedAt = record.LastFailedAt,
            UpdatedAt = record.UpdatedAt
        };
    }
}
