using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.Maintenance;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
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
        var courses = await context.Courses
            .Where(course => course.SourceType == CourseSourceType.LocalFolder && course.CurrentLessonId != null)
            .ToListAsync(cancellationToken);

        var affectedItems = 0;
        foreach (var course in courses)
        {
            var currentLessonId = course.CurrentLessonId!.Value;
            var lessonExists = await context.Lessons.AnyAsync(
                lesson =>
                    lesson.Id == currentLessonId &&
                    lesson.SourceType == LessonSourceType.LocalFile &&
                    lesson.Topic != null &&
                    lesson.Topic.Module != null &&
                    lesson.Topic.Module.CourseId == course.Id,
                cancellationToken);

            if (lessonExists)
            {
                continue;
            }

            course.CurrentLessonId = null;
            affectedItems++;
        }

        if (affectedItems > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "StudyHub global maintenance completed. Operation: clear-broken-operational-state. ClearedCurrentLessons: {ClearedCurrentLessons}",
            affectedItems);

        return new AppMaintenanceOperationResult
        {
            Success = true,
            OperationKey = "clear-broken-operational-state",
            Message = affectedItems == 0
                ? "Nenhum estado operacional quebrado foi encontrado na manutencao global."
                : "Estados operacionais quebrados foram normalizados sem tocar em cursos ou arquivos locais.",
            AffectedItems = affectedItems
        };
    }
}
