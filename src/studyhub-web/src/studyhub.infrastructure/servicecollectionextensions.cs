using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.services;

namespace studyhub.infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStudyHubPersistence(this IServiceCollection services, string databasePath)
    {
        var connectionString = $"Data Source={databasePath}";

        services.AddSingleton<IStoragePathsService>(_ => new StoragePathsService(databasePath));
        services.AddDbContextFactory<StudyHubDbContext>(options =>
            options.UseSqlite(connectionString));
        services.AddSingleton<StudyHubDatabaseInitializer>();
        services.AddSingleton<IAppBackupService, AppBackupService>();
        services.AddSingleton<IAppMaintenanceService, AppMaintenanceService>();
        services.AddSingleton<ICourseService, PersistedCourseService>();
        services.AddSingleton<ICourseResumeService, CourseResumeService>();
        services.AddSingleton<ICourseGenerationHistoryService, CourseGenerationHistoryService>();
        services.AddSingleton<ICourseMaintenanceService, CourseMaintenanceService>();
        services.AddSingleton<ILocalFolderCourseBuilder, LocalFolderCourseBuilder>();
        services.AddSingleton<IOnlineCuratedCourseBuilder, OnlineCuratedCourseBuilder>();
        services.AddSingleton<IGeminiCourseProvider, GeminiCourseProvider>();
        services.AddSingleton<IYouTubeDiscoveryProvider, YouTubeDiscoveryProvider>();
        services.AddSingleton<IProviderValidationService, ProviderValidationService>();
        services.AddSingleton<CourseTextEnrichmentService>();
        services.AddSingleton<OnlineCourseCreationOrchestrator>();
        services.AddSingleton<IOnlineCourseCreationOrchestrator>(provider => provider.GetRequiredService<OnlineCourseCreationOrchestrator>());
        services.AddSingleton<IOnlineCourseOperationsService, OnlineCourseOperationsService>();
        services.AddSingleton<ICourseEnrichmentOrchestrator, CourseEnrichmentOrchestrator>();
        services.AddSingleton<ILocalCourseImportService, LocalCourseImportService>();
        services.AddSingleton<IRoutineService, RoutineService>();
        services.AddSingleton<IProgressService, PersistedProgressService>();
        services.AddSingleton<IExternalLessonRuntimeStateService, PersistedExternalLessonRuntimeStateService>();
        services.AddSingleton<IRoadmapService, PersistedRoadmapService>();
        services.AddSingleton<SupplementaryMaterialsService>();
        services.AddSingleton<ISupplementaryMaterialsService>(provider => provider.GetRequiredService<SupplementaryMaterialsService>());
        services.AddSingleton<IMaterialService>(provider => provider.GetRequiredService<SupplementaryMaterialsService>());

        return services;
    }
}
