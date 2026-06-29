using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using studyhub.application.Interfaces;
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
        services.AddSingleton<ILocalFolderCourseBuilder, LocalFolderCourseBuilder>();
        services.AddSingleton<ILocalCourseImportService, LocalCourseImportService>();
        services.AddSingleton<IRoutineService, RoutineService>();
        services.AddSingleton<IProgressService, PersistedProgressService>();

        return services;
    }
}
