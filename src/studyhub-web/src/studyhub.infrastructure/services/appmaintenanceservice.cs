using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.Maintenance;
using studyhub.application.Interfaces;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public sealed class AppMaintenanceService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    ILogger<AppMaintenanceService> logger) : IAppMaintenanceService
{
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ILogger<AppMaintenanceService> _logger = logger;

    public async Task<AppMaintenanceOperationResult> ClearBrokenOperationalStateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("StudyHub global maintenance started. Operation: clear-broken-operational-state");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var utcNow = DateTime.UtcNow;
        var staleThreshold = utcNow.AddMinutes(-30);

        var validCourseIds = await context.Courses
            .Select(course => course.Id)
            .ToHashSetAsync(cancellationToken);
        var validLessonIds = await context.Lessons
            .Select(lesson => lesson.Id)
            .ToHashSetAsync(cancellationToken);

        var orphanCourseSteps = await context.CourseGenerationSteps
            .Where(item => !validCourseIds.Contains(item.CourseId))
            .ToListAsync(cancellationToken);

        var staleRunningSteps = await context.CourseGenerationSteps
            .Where(item =>
                string.Equals(item.Status, "Running", StringComparison.OrdinalIgnoreCase) &&
                item.CreatedAt < staleThreshold)
            .ToListAsync(cancellationToken);

        var orphanExternalStates = await context.ExternalLessonRuntimeStates
            .Where(item => !validCourseIds.Contains(item.CourseId) || !validLessonIds.Contains(item.LessonId))
            .ToListAsync(cancellationToken);

        var staleExternalStates = await context.ExternalLessonRuntimeStates
            .Where(item =>
                string.Equals(item.Status, "Opened", StringComparison.OrdinalIgnoreCase) &&
                item.UpdatedAt < staleThreshold)
            .ToListAsync(cancellationToken);

        if (orphanCourseSteps.Count > 0)
        {
            context.CourseGenerationSteps.RemoveRange(orphanCourseSteps);
        }

        if (orphanExternalStates.Count > 0)
        {
            context.ExternalLessonRuntimeStates.RemoveRange(orphanExternalStates);
        }

        foreach (var step in staleRunningSteps)
        {
            step.Status = "Failed";
            step.ErrorMessage = "Operacao interrompida por encerramento do app ou falha parcial.";
            step.LastFailedAt = utcNow;
            step.LastErrorMessage = step.ErrorMessage;
            step.CreatedAt = utcNow;
        }

        foreach (var state in staleExternalStates)
        {
            state.Status = "Failed";
            state.LastErrorCode = "stale-runtime-state";
            state.LastErrorMessage = "Runtime externo interrompido antes de concluir a abertura da aula.";
            state.LastFailedAt = utcNow;
            state.UpdatedAt = utcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        var affectedItems = orphanCourseSteps.Count + orphanExternalStates.Count + staleRunningSteps.Count + staleExternalStates.Count;
        _logger.LogInformation(
            "StudyHub global maintenance completed. Operation: clear-broken-operational-state. OrphanSteps: {OrphanSteps}. StaleRunningSteps: {StaleRunningSteps}. OrphanExternalStates: {OrphanExternalStates}. StaleExternalStates: {StaleExternalStates}",
            orphanCourseSteps.Count,
            staleRunningSteps.Count,
            orphanExternalStates.Count,
            staleExternalStates.Count);

        return new AppMaintenanceOperationResult
        {
            Success = true,
            OperationKey = "clear-broken-operational-state",
            Message = affectedItems == 0
                ? "Nenhum estado operacional quebrado foi encontrado na manutencao global."
                : "Estados operacionais quebrados foram normalizados sem tocar nos artefatos validos.",
            AffectedItems = affectedItems
        };
    }
}
