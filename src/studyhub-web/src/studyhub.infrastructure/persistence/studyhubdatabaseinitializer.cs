using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;

namespace studyhub.infrastructure.persistence;

public class StudyHubDatabaseInitializer(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    IStoragePathsService storagePathsService,
    ILogger<StudyHubDatabaseInitializer> logger)
{
    private const int CurrentSchemaVersion = 12;

    private static readonly JsonSerializerOptions SourceMetadataJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly StringComparer StructuralPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly string[] RequiredTables =
    [
        "courses",
        "modules",
        "topics",
        "lessons",
        "course_import_snapshots"
    ];

    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly IStoragePathsService _storagePathsService = storagePathsService;
    private readonly ILogger<StudyHubDatabaseInitializer> _logger = logger;

    public async Task InitializeAsync()
    {
        _storagePathsService.EnsureStorageDirectories();
        var databasePath = _storagePathsService.DatabasePath;
        var startupMode = DatabaseExists(databasePath) ? "existing" : "new";

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

    private async Task ApplySchemaUpgradesAsync(StudyHubDbContext context)
    {
        await EnsureCoursePresentationColumnsAsync(context);
        await EnsureModuleDescriptionColumnAsync(context);
        await EnsureModulePresentationColumnsAsync(context);
        await EnsureTopicDescriptionColumnAsync(context);
        await EnsureTopicPresentationColumnsAsync(context);
        await EnsureTopicCompletionColumnAsync(context);
        await EnsureLessonsPlaybackColumnAsync(context);
        await EnsureLessonDescriptionColumnAsync(context);
        await EnsureLessonPresentationColumnsAsync(context);
        await EnsureCourseSourceColumnsAsync(context);
        await EnsureCourseLifecycleStatusColumnAsync(context);
        await EnsureLessonSourceColumnsAsync(context);
        await EnsureLessonRelativeFilePathColumnAsync(context);
        await EnsureModuleSourceRelativePathColumnAsync(context);
        await EnsureTopicSourceRelativePathColumnAsync(context);
        await BackfillLegacyCourseOriginDataAsync(context);
        await BackfillLegacyPresentationDataAsync(context);
        await BackfillLegacyLessonOriginDataAsync(context);
        await BackfillLegacyLessonRelativeFilePathsAsync(context);
        await BackfillLegacyStructuralSourceRelativePathsAsync(context);
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

    private static async Task EnsureTopicCompletionColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "topics", "completed_at_utc"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE topics ADD COLUMN completed_at_utc TEXT NULL;
            """);
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

    private static async Task EnsureCourseLifecycleStatusColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "courses", "lifecycle_status"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE courses ADD COLUMN lifecycle_status INTEGER NOT NULL DEFAULT 0;
            """);
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

        if (!await ColumnExistsAsync(context, "lessons", "provider"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE lessons ADD COLUMN provider TEXT NOT NULL DEFAULT '';
                """);
        }
    }

    private static async Task EnsureLessonRelativeFilePathColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "lessons", "relative_file_path"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE lessons ADD COLUMN relative_file_path TEXT NOT NULL DEFAULT '';
            """);
    }

    private static async Task EnsureModuleSourceRelativePathColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "modules", "source_relative_path"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE modules ADD COLUMN source_relative_path TEXT NOT NULL DEFAULT '';
            """);
    }

    private static async Task EnsureTopicSourceRelativePathColumnAsync(StudyHubDbContext context)
    {
        if (await ColumnExistsAsync(context, "topics", "source_relative_path"))
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE topics ADD COLUMN source_relative_path TEXT NOT NULL DEFAULT '';
            """);
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
                RootPath = course.SourceType == CourseSourceType.LocalFolder ? course.FolderPath : string.Empty,
                ImportedAt = course.AddedAt == default ? DateTime.UtcNow : course.AddedAt,
                ScanVersion = course.SourceType == CourseSourceType.LocalFolder
                    ? "legacy-local-folder-v1"
                    : "legacy-hidden-source-v1",
                Provider = course.SourceType == CourseSourceType.LocalFolder
                    ? "LocalFileSystem"
                    : "LegacyHidden"
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

    private async Task BackfillLegacyLessonRelativeFilePathsAsync(StudyHubDbContext context)
    {
        var courses = await context.Courses
            .Where(course => course.SourceType == CourseSourceType.LocalFolder)
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .ToListAsync();

        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var course in courses)
        {
            var lessonsToBackfill = course.Modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Where(lesson =>
                    lesson.SourceType == LessonSourceType.LocalFile &&
                    string.IsNullOrWhiteSpace(lesson.RelativeFilePath))
                .ToList();

            if (lessonsToBackfill.Count == 0)
            {
                continue;
            }

            var rootPath = ResolveCourseRootPath(course);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                skippedCount += lessonsToBackfill.Count;
                continue;
            }

            foreach (var lesson in lessonsToBackfill)
            {
                var absoluteLessonPath = string.IsNullOrWhiteSpace(lesson.LocalFilePath)
                    ? lesson.FilePath
                    : lesson.LocalFilePath;

                if (!LocalLessonPathHelper.TryCalculatePortableRelativePath(
                        rootPath,
                        absoluteLessonPath,
                        out var relativeFilePath))
                {
                    skippedCount++;
                    continue;
                }

                lesson.RelativeFilePath = relativeFilePath;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "StudyHub lesson relative-path backfill completed. Updated: {UpdatedCount}. Skipped unsafe or invalid: {SkippedCount}.",
            updatedCount,
            skippedCount);
    }

    private async Task BackfillLegacyStructuralSourceRelativePathsAsync(StudyHubDbContext context)
    {
        var courses = await context.Courses
            .Where(course => course.SourceType == CourseSourceType.LocalFolder)
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .ToListAsync();

        if (courses.Count == 0)
        {
            return;
        }

        var courseIds = courses.Select(course => course.Id).ToHashSet();
        var snapshots = (await context.CourseImportSnapshots
                .AsNoTracking()
                .Where(snapshot => courseIds.Contains(snapshot.CourseId))
                .ToListAsync())
            .ToDictionary(snapshot => snapshot.CourseId);

        var updatedModules = 0;
        var updatedTopics = 0;
        var snapshotUpdates = 0;
        var fallbackUpdates = 0;
        var canonicalizedValues = 0;
        var clearedInvalidValues = 0;

        foreach (var course in courses)
        {
            var snapshotModulePaths = new Dictionary<Guid, string>();
            var snapshotTopicPaths = new Dictionary<Guid, string>();

            if (snapshots.TryGetValue(course.Id, out var snapshot))
            {
                TryBuildSnapshotStructuralPathCandidates(
                    snapshot.StructureJson,
                    course,
                    snapshotModulePaths,
                    snapshotTopicPaths);
            }

            var rootPath = ResolveCourseRootPath(course);

            foreach (var module in course.Modules)
            {
                foreach (var topic in module.Topics)
                {
                    if (LocalCourseStructurePathHelper.TryNormalize(
                            topic.SourceRelativePath,
                            out var normalizedExistingTopicPath))
                    {
                        if (!string.Equals(
                                topic.SourceRelativePath,
                                normalizedExistingTopicPath,
                                StringComparison.Ordinal))
                        {
                            topic.SourceRelativePath = normalizedExistingTopicPath;
                            updatedTopics++;
                            canonicalizedValues++;
                        }

                        continue;
                    }

                    var usedSnapshot = snapshotTopicPaths.TryGetValue(topic.Id, out var candidatePath);
                    var hasCandidate = usedSnapshot ||
                                       TryInferTopicSourceRelativePath(topic, rootPath, out candidatePath);
                    var replacement = hasCandidate ? candidatePath! : string.Empty;

                    if (string.Equals(topic.SourceRelativePath, replacement, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!hasCandidate && !string.IsNullOrWhiteSpace(topic.SourceRelativePath))
                    {
                        clearedInvalidValues++;
                    }

                    topic.SourceRelativePath = replacement;
                    updatedTopics++;
                    snapshotUpdates += usedSnapshot ? 1 : 0;
                    fallbackUpdates += hasCandidate && !usedSnapshot ? 1 : 0;
                }

                if (LocalCourseStructurePathHelper.TryNormalize(
                        module.SourceRelativePath,
                        out var normalizedExistingModulePath))
                {
                    if (!string.Equals(
                            module.SourceRelativePath,
                            normalizedExistingModulePath,
                            StringComparison.Ordinal))
                    {
                        module.SourceRelativePath = normalizedExistingModulePath;
                        updatedModules++;
                        canonicalizedValues++;
                    }

                    continue;
                }

                var usedModuleSnapshot = snapshotModulePaths.TryGetValue(module.Id, out var moduleCandidatePath);
                var hasModuleCandidate = usedModuleSnapshot ||
                                         TryInferModuleSourceRelativePath(module, rootPath, out moduleCandidatePath);
                var moduleReplacement = hasModuleCandidate ? moduleCandidatePath! : string.Empty;

                if (string.Equals(module.SourceRelativePath, moduleReplacement, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!hasModuleCandidate && !string.IsNullOrWhiteSpace(module.SourceRelativePath))
                {
                    clearedInvalidValues++;
                }

                module.SourceRelativePath = moduleReplacement;
                updatedModules++;
                snapshotUpdates += usedModuleSnapshot ? 1 : 0;
                fallbackUpdates += hasModuleCandidate && !usedModuleSnapshot ? 1 : 0;
            }
        }

        if (updatedModules > 0 || updatedTopics > 0)
        {
            await context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "StudyHub structural source-path backfill completed. Modules updated: {UpdatedModules}. Topics updated: {UpdatedTopics}. Snapshot values: {SnapshotUpdates}. Lesson fallbacks: {FallbackUpdates}. Canonicalized values: {CanonicalizedValues}. Invalid values cleared: {ClearedInvalidValues}.",
            updatedModules,
            updatedTopics,
            snapshotUpdates,
            fallbackUpdates,
            canonicalizedValues,
            clearedInvalidValues);
    }

    private static bool TryBuildSnapshotStructuralPathCandidates(
        string? structureJson,
        CourseRecord course,
        IDictionary<Guid, string> modulePaths,
        IDictionary<Guid, string> topicPaths)
    {
        if (!TryDeserializeManifest(structureJson, out var manifest) ||
            !LocalCourseManifestValidator.TryCorrelatePersistedIdentities(
                manifest,
                course,
                out var identityCorrelation) ||
            !identityCorrelation.IsExactMatch)
        {
            return false;
        }

        var persistedLessons = course.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .ToDictionary(lesson => lesson.Id);
        var rootPath = ResolveCourseRootPath(course);

        foreach (var detectedModule in manifest.Modules)
        {
            if (!identityCorrelation.MatchesModule(detectedModule.ModuleId) ||
                !LocalCourseStructurePathHelper.TryNormalize(
                    detectedModule.RelativePath,
                    out var moduleSourceRelativePath))
            {
                continue;
            }

            var hasTrustedTopic = false;

            foreach (var detectedTopic in detectedModule.Topics)
            {
                if (!identityCorrelation.MatchesTopic(
                        detectedModule.ModuleId,
                        detectedTopic.TopicId) ||
                    !LocalCourseStructurePathHelper.TryCombine(
                        detectedModule.RelativePath,
                        detectedTopic.RelativePath,
                        out var topicSourceRelativePath) ||
                    !HasCoherentSnapshotTopicPath(
                        detectedTopic,
                        topicSourceRelativePath,
                        identityCorrelation,
                        persistedLessons,
                        rootPath))
                {
                    continue;
                }

                topicPaths[detectedTopic.TopicId] = topicSourceRelativePath;
                hasTrustedTopic = true;
            }

            if (hasTrustedTopic)
            {
                modulePaths[detectedModule.ModuleId] = moduleSourceRelativePath;
            }
        }

        return modulePaths.Count > 0 || topicPaths.Count > 0;
    }

    private static bool HasCoherentSnapshotTopicPath(
        DetectedTopicStructure detectedTopic,
        string topicSourceRelativePath,
        LocalCourseManifestIdentityCorrelation identityCorrelation,
        IReadOnlyDictionary<Guid, LessonRecord> persistedLessons,
        string rootPath)
    {
        if (detectedTopic.Lessons.Count == 0)
        {
            return false;
        }

        foreach (var detectedLesson in detectedTopic.Lessons)
        {
            if (!identityCorrelation.MatchesLesson(detectedTopic.TopicId, detectedLesson.LessonId) ||
                !persistedLessons.TryGetValue(detectedLesson.LessonId, out var persistedLesson) ||
                !LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                    detectedLesson.RelativePath,
                    out var lessonDirectory) ||
                !StructuralPathComparer.Equals(lessonDirectory, topicSourceRelativePath))
            {
                return false;
            }

            if (TryGetComparableLessonRelativePath(
                    persistedLesson,
                    rootPath,
                    out var persistedRelativeFilePath) &&
                (!LocalLessonPathHelper.TryNormalizePortableRelativePath(
                     detectedLesson.RelativePath,
                     out var manifestRelativeFilePath) ||
                 !StructuralPathComparer.Equals(
                     persistedRelativeFilePath,
                     manifestRelativeFilePath)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDeserializeManifest(
        string? structureJson,
        out DetectedCourseStructure manifest)
    {
        manifest = null!;

        if (string.IsNullOrWhiteSpace(structureJson))
        {
            return false;
        }

        try
        {
            manifest = JsonSerializer.Deserialize<DetectedCourseStructure>(
                structureJson,
                SourceMetadataJsonOptions)!;
            return LocalCourseManifestValidator.HasUsableStructure(manifest);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryInferTopicSourceRelativePath(
        TopicRecord topic,
        string rootPath,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        var localLessons = topic.Lessons
            .Where(lesson => lesson.SourceType == LessonSourceType.LocalFile)
            .ToList();

        if (localLessons.Count == 0)
        {
            return false;
        }

        string? commonDirectory = null;

        foreach (var lesson in localLessons)
        {
            if (!TryGetComparableLessonRelativePath(lesson, rootPath, out var relativeFilePath) ||
                !LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                    relativeFilePath,
                    out var lessonDirectory))
            {
                return false;
            }

            if (commonDirectory is null)
            {
                commonDirectory = lessonDirectory;
            }
            else if (!StructuralPathComparer.Equals(commonDirectory, lessonDirectory))
            {
                return false;
            }
        }

        return LocalCourseStructurePathHelper.TryNormalize(commonDirectory, out sourceRelativePath);
    }

    private static bool TryInferModuleSourceRelativePath(
        ModuleRecord module,
        string rootPath,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        var lessonDirectories = new List<string>();
        var localLessons = module.Topics
            .SelectMany(topic => topic.Lessons)
            .Where(lesson => lesson.SourceType == LessonSourceType.LocalFile)
            .ToList();

        foreach (var lesson in localLessons)
        {
            if (!TryGetComparableLessonRelativePath(lesson, rootPath, out var relativeFilePath) ||
                !LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
                    relativeFilePath,
                    out var lessonDirectory))
            {
                return false;
            }

            lessonDirectories.Add(lessonDirectory);
        }

        if (lessonDirectories.Count == 0)
        {
            foreach (var topic in module.Topics)
            {
                if (!LocalCourseStructurePathHelper.TryNormalize(
                        topic.SourceRelativePath,
                        out var topicSourceRelativePath))
                {
                    return false;
                }

                lessonDirectories.Add(topicSourceRelativePath);
            }
        }

        return TryInferModulePathFromDirectories(lessonDirectories, out sourceRelativePath);
    }

    private static bool TryInferModulePathFromDirectories(
        IReadOnlyCollection<string> directories,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        if (directories.Count == 0)
        {
            return false;
        }

        var normalizedDirectories = new HashSet<string>(StructuralPathComparer);

        foreach (var directory in directories)
        {
            if (!LocalCourseStructurePathHelper.TryNormalize(directory, out var normalizedDirectory))
            {
                return false;
            }

            normalizedDirectories.Add(normalizedDirectory);
        }

        if (normalizedDirectories.Contains("."))
        {
            sourceRelativePath = ".";
            return true;
        }

        if (normalizedDirectories.Count == 1)
        {
            var onlyDirectory = normalizedDirectories.Single();
            if (onlyDirectory.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }

            sourceRelativePath = onlyDirectory;
            return true;
        }

        return LocalCourseStructurePathHelper.TryGetCommonAncestor(
                   normalizedDirectories,
                   out var commonAncestor) &&
               commonAncestor != "." &&
               LocalCourseStructurePathHelper.TryNormalize(commonAncestor, out sourceRelativePath);
    }

    private static bool TryGetComparableLessonRelativePath(
        LessonRecord lesson,
        string rootPath,
        out string relativeFilePath)
    {
        if (LocalLessonPathHelper.TryNormalizePortableRelativePath(
                lesson.RelativeFilePath,
                out relativeFilePath))
        {
            return true;
        }

        var absoluteLessonPath = string.IsNullOrWhiteSpace(lesson.LocalFilePath)
            ? lesson.FilePath
            : lesson.LocalFilePath;

        return LocalLessonPathHelper.TryCalculatePortableRelativePath(
            rootPath,
            absoluteLessonPath,
            out relativeFilePath);
    }

    private static string ResolveCourseRootPath(CourseRecord course)
    {
        if (!string.IsNullOrWhiteSpace(course.SourceMetadataJson))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<CourseSourceMetadata>(
                    course.SourceMetadataJson,
                    SourceMetadataJsonOptions);

                if (LocalLessonPathHelper.TryNormalizeFullyQualifiedPath(metadata?.RootPath, out var metadataRootPath))
                {
                    return metadataRootPath;
                }
            }
            catch (JsonException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return LocalLessonPathHelper.TryNormalizeFullyQualifiedPath(course.FolderPath, out var folderPath)
            ? folderPath
            : string.Empty;
    }
}
