using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using studyhub.application.Interfaces;
using studyhub.infrastructure;
using studyhub.infrastructure.persistence;
using studyhub.app.state;
using studyhub.app.services;

namespace studyhub.app;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkitMediaElement(false)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<CourseCatalogState>();
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
        builder.Services.AddSingleton<IVideoMetadataReader, VideoMetadataReader>();
        builder.Services.AddSingleton<NativeLessonPlaybackService>();
        builder.Services.AddSingleton<ExternalLessonPlaybackService>();
        builder.Services.AddSingleton<IIntegrationSettingsService, SecureStorageIntegrationSettingsService>();
        builder.Services.AddSingleton<CourseIntentPromptService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "studyhub.db");
        builder.Services.AddStudyHubPersistence(databasePath);

        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var storagePaths = scope.ServiceProvider.GetRequiredService<IStoragePathsService>();
        storagePaths.EnsureStorageDirectories();
        var databaseInitializer = scope.ServiceProvider.GetRequiredService<StudyHubDatabaseInitializer>();
        databaseInitializer.InitializeAsync().GetAwaiter().GetResult();

        return app;
    }
}
