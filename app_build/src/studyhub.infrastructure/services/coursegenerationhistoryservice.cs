using Microsoft.EntityFrameworkCore;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Interfaces;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public class CourseGenerationHistoryService(IDbContextFactory<StudyHubDbContext> contextFactory) : ICourseGenerationHistoryService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;

    public async Task RecordStepAsync(CourseGenerationStepEntry entry, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var timestamp = entry.CreatedAt == default ? DateTime.UtcNow : entry.CreatedAt;
        var record = await context.CourseGenerationSteps
            .Where(item => item.CourseId == entry.CourseId && item.StepKey == entry.StepKey)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record == null)
        {
            record = new CourseGenerationStepRecord
            {
                Id = Guid.NewGuid(),
                CourseId = entry.CourseId,
                StepKey = entry.StepKey
            };

            await context.CourseGenerationSteps.AddAsync(record, cancellationToken);
        }

        record.Provider = entry.Provider;
        record.Status = entry.Status.ToString();
        record.RequestJson = entry.RequestJson ?? string.Empty;
        record.ResponseJson = entry.ResponseJson ?? string.Empty;
        record.ErrorMessage = entry.ErrorMessage ?? string.Empty;
        record.CreatedAt = timestamp;
        record.LastSucceededAt = entry.LastSucceededAt ?? record.LastSucceededAt;
        record.LastFailedAt = entry.LastFailedAt ?? record.LastFailedAt;
        record.LastErrorMessage = entry.LastErrorMessage ?? record.LastErrorMessage ?? string.Empty;

        switch (entry.Status)
        {
            case CourseGenerationStepStatus.Succeeded:
                record.LastSucceededAt = timestamp;
                break;
            case CourseGenerationStepStatus.Failed:
                record.LastFailedAt = timestamp;
                record.LastErrorMessage = string.IsNullOrWhiteSpace(entry.ErrorMessage)
                    ? entry.LastErrorMessage ?? record.LastErrorMessage ?? string.Empty
                    : entry.ErrorMessage;
                break;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, CourseGenerationStepEntry>> GetStepStatesAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.CourseGenerationSteps
            .AsNoTracking()
            .Where(item => item.CourseId == courseId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(item => item.StepKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var record = group.First();
                    return new CourseGenerationStepEntry
                    {
                        CourseId = record.CourseId,
                        StepKey = record.StepKey,
                        Provider = record.Provider,
                        Status = Enum.TryParse<CourseGenerationStepStatus>(record.Status, true, out var status)
                            ? status
                            : CourseGenerationStepStatus.Failed,
                        RequestJson = record.RequestJson,
                        ResponseJson = record.ResponseJson,
                        ErrorMessage = record.ErrorMessage,
                        CreatedAt = record.CreatedAt,
                        LastSucceededAt = record.LastSucceededAt,
                        LastFailedAt = record.LastFailedAt,
                        LastErrorMessage = record.LastErrorMessage
                    };
                },
                StringComparer.OrdinalIgnoreCase);
    }
}
