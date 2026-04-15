using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.persistence;

public class StudyHubDatabaseInitializer(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IStoragePathsService storagePathsService,
    ILogger<StudyHubDatabaseInitializer> logger)
{
    private const int CurrentSchemaVersion = 7;

    private static readonly string[] RequiredTables =
    [
        "courses",
        "modules",
        "topics",
        "lessons",
        "course_import_snapshots",
        "course_roadmaps",
        "course_materials"
    ];

    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IStoragePathsService _storagePathsService = storagePathsService;
    private readonly ILogger<StudyHubDatabaseInitializer> _logger = logger;

    public async Task InitializeAsync()
    {
        _storagePathsService.EnsureStorageDirectories();
        var databasePath = _storagePathsService.DatabasePath;
        var startupMode = DatabaseExists(databasePath)
            ? "existing"
            : "new";

        _logger.LogInformation(
            "StudyHub database initialization started. Mode: {StartupMode}. Path: {DatabasePath}. BackupRoot: {BackupRoot}. RoutineRoot: {RoutineRoot}",
            startupMode,
            databasePath,
            _storagePathsService.BackupsDirectory,
            _storagePathsService.RoutineDirectory);

        try
        {
            await InitializeDatabaseCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StudyHub database startup failed during schema initialization. Attempting non-destructive recovery first.");

            if (await TryRecoverStartupStateAsync(databasePath, cancellationToken: default))
            {
                _logger.LogWarning(
                    "StudyHub database startup recovered without destroying local data. Path: {DatabasePath}",
                    databasePath);
                return;
            }

            _logger.LogError("StudyHub database recovery was not possible. Falling back to controlled reset.");

            var backupPath = await BackupIncompatibleDatabaseAsync(databasePath);
            _logger.LogWarning(
                "StudyHub local database was reset after a schema startup failure. Backup: {BackupPath}",
                backupPath ?? "<database file not found>");

            try
            {
                await InitializeDatabaseCoreAsync();
            }
            catch (Exception recoveryEx)
            {
                _logger.LogCritical(recoveryEx, "StudyHub database recovery failed after controlled reset.");
                throw;
            }
        }
    }

    private async Task InitializeDatabaseCoreAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var createdNewDatabase = await context.Database.EnsureCreatedAsync();
        _logger.LogInformation(
            "StudyHub database EnsureCreated completed. Created new database: {CreatedNewDatabase}",
            createdNewDatabase);

        await ValidateRequiredTablesAsync(context);
        await NormalizeSchemaVersionAsync(context);
        await ApplySchemaUpgradesAsync(context);
        await SetSchemaVersionAsync(context, CurrentSchemaVersion);
        await SeedRoadmapsAsync(context);
        await SeedMaterialsAsync(context);
    }

    private async Task<bool> TryRecoverStartupStateAsync(string? databasePath, CancellationToken cancellationToken)
    {
        if (!DatabaseExists(databasePath))
        {
            return false;
        }

        try
        {
            SqliteConnection.ClearAllPools();

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var integrityCheck = await RunIntegrityCheckAsync(context, cancellationToken);
            if (!string.Equals(integrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "StudyHub database integrity check failed during startup recovery. Path: {DatabasePath}. Result: {IntegrityCheck}",
                    databasePath,
                    integrityCheck);
                return false;
            }

            _logger.LogWarning(
                "StudyHub database integrity check succeeded. Retrying non-destructive initialization. Path: {DatabasePath}",
                databasePath);

            await InitializeDatabaseCoreAsync();
            return true;
        }
        catch (Exception recoveryEx)
        {
            _logger.LogWarning(
                recoveryEx,
                "StudyHub non-destructive startup recovery failed. Path: {DatabasePath}",
                databasePath);
            return false;
        }
    }

    private async Task<string?> BackupIncompatibleDatabaseAsync(string? databasePath)
    {
        databasePath ??= await ResolveConfiguredDatabasePathAsync();
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return null;
        }

        SqliteConnection.ClearAllPools();

        if (!File.Exists(databasePath))
        {
            return null;
        }

        var backupPath = BuildBackupPath(databasePath);
        BackupCompanionFile(databasePath, backupPath, string.Empty);
        BackupCompanionFile(databasePath, backupPath, "-wal");
        BackupCompanionFile(databasePath, backupPath, "-shm");
        BackupCompanionFile(databasePath, backupPath, "-journal");

        return backupPath;
    }

    private async Task<string?> ResolveConfiguredDatabasePathAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var dataSource = context.Database.GetDbConnection().DataSource;

        return string.IsNullOrWhiteSpace(dataSource)
            ? null
            : Path.GetFullPath(dataSource);
    }

    private static bool DatabaseExists(string? databasePath)
        => !string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath);

    private static string BuildBackupPath(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(databasePath);
        var extension = Path.GetExtension(databasePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var candidate = Path.Combine(directory, $"{fileNameWithoutExtension}.backup-{timestamp}{extension}");
        var attempt = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileNameWithoutExtension}.backup-{timestamp}-{attempt++}{extension}");
        }

        return candidate;
    }

    private static void BackupCompanionFile(string databasePath, string backupPath, string suffix)
    {
        var source = databasePath + suffix;
        if (!File.Exists(source))
        {
            return;
        }

        File.Move(source, backupPath + suffix);
    }

    private static async Task ValidateRequiredTablesAsync(StudyHubDbContext context)
    {
        foreach (var tableName in RequiredTables)
        {
            if (await TableExistsAsync(context, tableName))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"StudyHub database schema is incompatible because required table '{tableName}' is missing.");
        }
    }

    private async Task NormalizeSchemaVersionAsync(StudyHubDbContext context)
    {
        var detectedSchemaVersion = await GetSchemaVersionAsync(context);

        if (detectedSchemaVersion == CurrentSchemaVersion)
        {
            _logger.LogInformation(
                "StudyHub database schema version already matches the current version {SchemaVersion}.",
                CurrentSchemaVersion);
            return;
        }

        _logger.LogInformation(
            "StudyHub database schema version detected as {DetectedSchemaVersion}. Current version is {CurrentSchemaVersion}.",
            detectedSchemaVersion,
            CurrentSchemaVersion);

        if (detectedSchemaVersion > CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "StudyHub database schema version {DetectedSchemaVersion} is newer than the app schema version {CurrentSchemaVersion}. The database will be restamped with the current app schema version after validation.",
                detectedSchemaVersion,
                CurrentSchemaVersion);
        }
    }

    private static async Task ApplySchemaUpgradesAsync(StudyHubDbContext context)
    {
        await EnsureCourseGenerationRunsTableAsync(context);
        await EnsureCourseGenerationRunStateColumnsAsync(context);
        await EnsureExternalLessonRuntimeStatesTableAsync(context);
        await EnsureExternalCourseImportsTableAsync(context);
        await EnsureExternalAssessmentsTableAsync(context);
        await EnsureCoursePresentationColumnsAsync(context);
        await EnsureModuleDescriptionColumnAsync(context);
        await EnsureModulePresentationColumnsAsync(context);
        await EnsureTopicDescriptionColumnAsync(context);
        await EnsureTopicPresentationColumnsAsync(context);
        await EnsureLessonsPlaybackColumnAsync(context);
        await EnsureLessonDescriptionColumnAsync(context);
        await EnsureLessonPresentationColumnsAsync(context);
        await EnsureCourseSourceColumnsAsync(context);
        await EnsureLessonSourceColumnsAsync(context);
        await BackfillLegacyCourseOriginDataAsync(context);
        await BackfillLegacyPresentationDataAsync(context);
        await BackfillLegacyLessonOriginDataAsync(context);
    }

    private static async Task EnsureCoursePresentationColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "courses", "raw_title"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE courses ADD COLUMN raw_title TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!await ColumnExistsAsync(context, "courses", "raw_description"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE courses ADD COLUMN raw_description TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureCourseGenerationRunsTableAsync(StudyHubDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS course_generation_runs (
                id TEXT NOT NULL PRIMARY KEY,
                course_id TEXT NOT NULL,
                step_key TEXT NOT NULL,
                provider TEXT NOT NULL,
                status TEXT NOT NULL,
                request_json TEXT NOT NULL DEFAULT '',
                response_json TEXT NOT NULL DEFAULT '',
                error_message TEXT NOT NULL DEFAULT '',
                last_succeeded_at TEXT NULL,
                last_failed_at TEXT NULL,
                last_error_message TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL
            );
            """);
    }

    private static async Task EnsureCourseGenerationRunStateColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "course_generation_runs", "last_succeeded_at"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE course_generation_runs ADD COLUMN last_succeeded_at TEXT NULL;
                """);
        }

        if (!await ColumnExistsAsync(context, "course_generation_runs", "last_failed_at"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE course_generation_runs ADD COLUMN last_failed_at TEXT NULL;
                """);
        }

        if (!await ColumnExistsAsync(context, "course_generation_runs", "last_error_message"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE course_generation_runs ADD COLUMN last_error_message TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureExternalLessonRuntimeStatesTableAsync(StudyHubDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS external_lesson_runtime_states (
                lesson_id TEXT NOT NULL PRIMARY KEY,
                course_id TEXT NOT NULL,
                provider TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT '',
                external_url TEXT NOT NULL DEFAULT '',
                last_error_code TEXT NOT NULL DEFAULT '',
                last_error_message TEXT NOT NULL DEFAULT '',
                fallback_launched INTEGER NOT NULL DEFAULT 0,
                last_opened_at TEXT NULL,
                last_succeeded_at TEXT NULL,
                last_failed_at TEXT NULL,
                updated_at TEXT NOT NULL
            );
            """);
    }

    private static async Task EnsureExternalCourseImportsTableAsync(StudyHubDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS external_course_imports (
                course_id TEXT NOT NULL PRIMARY KEY,
                schema_version TEXT NOT NULL DEFAULT '',
                source_kind TEXT NOT NULL DEFAULT '',
                source_system TEXT NOT NULL DEFAULT '',
                provider TEXT NOT NULL DEFAULT '',
                external_course_id TEXT NOT NULL DEFAULT '',
                payload_fingerprint TEXT NOT NULL DEFAULT '',
                origin_url TEXT NOT NULL DEFAULT '',
                payload_json TEXT NOT NULL DEFAULT '',
                imported_at TEXT NOT NULL,
                FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE
            );
            """);
    }

    private static async Task EnsureExternalAssessmentsTableAsync(StudyHubDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS external_assessments (
                id TEXT NOT NULL PRIMARY KEY,
                course_id TEXT NOT NULL,
                discipline_external_id TEXT NOT NULL DEFAULT '',
                assessment_external_id TEXT NOT NULL DEFAULT '',
                assessment_type TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT '',
                weight_percentage REAL NULL,
                availability_start_at TEXT NULL,
                availability_end_at TEXT NULL,
                grade REAL NULL,
                metadata_json TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL,
                FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE
            );
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS ix_external_assessments_course_id
            ON external_assessments (course_id);
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS ix_external_assessments_course_id_assessment_external_id
            ON external_assessments (course_id, assessment_external_id);
            """);
    }

    private static async Task EnsureModuleDescriptionColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "modules", "description"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE modules ADD COLUMN description TEXT NOT NULL DEFAULT '';
            """);
    }

    private static async Task EnsureTopicDescriptionColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "topics", "description"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE topics ADD COLUMN description TEXT NOT NULL DEFAULT '';
            """);
    }

    private static async Task EnsureModulePresentationColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "modules", "raw_title"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE modules ADD COLUMN raw_title TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!await ColumnExistsAsync(context, "modules", "raw_description"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE modules ADD COLUMN raw_description TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureTopicPresentationColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "topics", "raw_title"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE topics ADD COLUMN raw_title TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!await ColumnExistsAsync(context, "topics", "raw_description"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE topics ADD COLUMN raw_description TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureLessonsPlaybackColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "lessons", "last_playback_position_seconds"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE lessons ADD COLUMN last_playback_position_seconds INTEGER NOT NULL DEFAULT 0;
            """);
    }

    private static async Task EnsureLessonDescriptionColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "lessons", "description"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE lessons ADD COLUMN description TEXT NOT NULL DEFAULT '';
            """);
    }

    private static async Task EnsureLessonPresentationColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "lessons", "raw_title"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN raw_title TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!await ColumnExistsAsync(context, "lessons", "raw_description"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN raw_description TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureCourseSourceColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "courses", "source_type"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE courses ADD COLUMN source_type INTEGER NOT NULL DEFAULT 0;
                """);
        }

        if (!await ColumnExistsAsync(context, "courses", "source_metadata_json"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE courses ADD COLUMN source_metadata_json TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureLessonSourceColumnsAsync(StudyHubDbContext context)
    {
        if (!await ColumnExistsAsync(context, "lessons", "source_type"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN source_type INTEGER NOT NULL DEFAULT 0;
                """);
        }

        if (!await ColumnExistsAsync(context, "lessons", "local_file_path"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN local_file_path TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!await ColumnExistsAsync(context, "lessons", "external_url"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN external_url TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!await ColumnExistsAsync(context, "lessons", "provider"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN provider TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task<bool> TableExistsAsync(StudyHubDbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "$tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(StudyHubDbContext context, string tableName, string columnName)
    {
        var columns = await GetTableColumnsAsync(context, tableName);
        return columns.Contains(columnName);
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(StudyHubDbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<int> GetSchemaVersionAsync(StudyHubDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        var result = await command.ExecuteScalarAsync();
        return result switch
        {
            long longValue => checked((int)longValue),
            int intValue => intValue,
            _ => 0
        };
    }

    private static async Task SetSchemaVersionAsync(StudyHubDbContext context, int version)
    {
        var boundedVersion = Math.Max(0, version);
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {boundedVersion};";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> RunIntegrityCheckAsync(StudyHubDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString() ?? "unknown";
    }

    private static async Task BackfillLegacyCourseOriginDataAsync(StudyHubDbContext context)
    {
        var courses = await context.Courses.ToListAsync();
        var changed = false;

        foreach (var course in courses)
        {
            if (!string.IsNullOrWhiteSpace(course.SourceMetadataJson))
            {
                continue;
            }

            var metadata = new CourseSourceMetadata
            {
                RootPath = course.FolderPath,
                ImportedAt = course.AddedAt == default ? DateTime.UtcNow : course.AddedAt,
                ScanVersion = course.SourceType switch
                {
                    CourseSourceType.OnlineCurated => "legacy-online-curated-v1",
                    CourseSourceType.ExternalImport => "legacy-external-import-v1",
                    _ => "legacy-local-folder-v1"
                },
                Provider = course.SourceType switch
                {
                    CourseSourceType.OnlineCurated => "YouTube",
                    CourseSourceType.ExternalImport => "ExternalImport",
                    _ => "LocalFileSystem"
                }
            };

            course.SourceMetadataJson = PersistenceMapper.SerializeCourseSourceMetadata(metadata);
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync();
        }
    }

    private static async Task BackfillLegacyPresentationDataAsync(StudyHubDbContext context)
    {
        var changed = false;

        var courses = await context.Courses.ToListAsync();
        foreach (var course in courses)
        {
            if (string.IsNullOrWhiteSpace(course.RawTitle))
            {
                course.RawTitle = course.Title;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(course.RawDescription))
            {
                course.RawDescription = course.Description;
                changed = true;
            }
        }

        var modules = await context.Modules.ToListAsync();
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.RawTitle))
            {
                module.RawTitle = module.Title;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(module.RawDescription))
            {
                module.RawDescription = module.Description;
                changed = true;
            }
        }

        var topics = await context.Topics.ToListAsync();
        foreach (var topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic.RawTitle))
            {
                topic.RawTitle = topic.Title;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(topic.RawDescription))
            {
                topic.RawDescription = topic.Description;
                changed = true;
            }
        }

        var lessons = await context.Lessons.ToListAsync();
        foreach (var lesson in lessons)
        {
            if (string.IsNullOrWhiteSpace(lesson.RawTitle))
            {
                lesson.RawTitle = lesson.Title;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(lesson.RawDescription))
            {
                lesson.RawDescription = lesson.Description;
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync();
        }
    }

    private static async Task BackfillLegacyLessonOriginDataAsync(StudyHubDbContext context)
    {
        var lessons = await context.Lessons.ToListAsync();
        var changed = false;

        foreach (var lesson in lessons)
        {
            if (string.IsNullOrWhiteSpace(lesson.LocalFilePath) && !string.IsNullOrWhiteSpace(lesson.FilePath))
            {
                lesson.LocalFilePath = lesson.FilePath;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(lesson.LocalFilePath) && lesson.SourceType != LessonSourceType.LocalFile)
            {
                lesson.SourceType = LessonSourceType.LocalFile;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(lesson.LocalFilePath) && string.IsNullOrWhiteSpace(lesson.Provider))
            {
                lesson.Provider = "LocalFileSystem";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(lesson.FilePath) && !string.IsNullOrWhiteSpace(lesson.LocalFilePath))
            {
                lesson.FilePath = lesson.LocalFilePath;
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedRoadmapsAsync(StudyHubDbContext context)
    {
        var existingCourseIds = await context.CourseRoadmaps
            .Select(record => record.CourseId)
            .ToListAsync();

        var missingCourseIds = await context.Courses
            .Where(record => !existingCourseIds.Contains(record.Id))
            .Select(record => record.Id)
            .ToListAsync();

        if (missingCourseIds.Count == 0)
        {
            return;
        }

        var records = new List<CourseRoadmapRecord>();

        foreach (var courseId in missingCourseIds)
        {
            records.Add(new CourseRoadmapRecord
            {
                CourseId = courseId,
                LevelsJson = PersistenceMapper.SerializeRoadmap([]),
                UpdatedAt = DateTime.Now
            });
        }

        await context.CourseRoadmaps.AddRangeAsync(records);
        await context.SaveChangesAsync();
    }

    private static async Task SeedMaterialsAsync(StudyHubDbContext context)
    {
        var existingCourseIds = await context.CourseMaterials
            .Select(record => record.CourseId)
            .ToListAsync();

        var missingCourseIds = await context.Courses
            .Where(record => !existingCourseIds.Contains(record.Id))
            .Select(record => record.Id)
            .ToListAsync();

        if (missingCourseIds.Count == 0)
        {
            return;
        }

        var records = new List<CourseMaterialsRecord>();

        foreach (var courseId in missingCourseIds)
        {
            records.Add(new CourseMaterialsRecord
            {
                CourseId = courseId,
                MaterialsJson = PersistenceMapper.SerializeMaterials([]),
                UpdatedAt = DateTime.Now
            });
        }

        await context.CourseMaterials.AddRangeAsync(records);
        await context.SaveChangesAsync();
    }
}
