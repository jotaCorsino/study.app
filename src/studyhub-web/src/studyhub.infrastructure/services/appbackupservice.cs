using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.Maintenance;
using studyhub.application.Interfaces;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public sealed class AppBackupService(
    IStoragePathsService storagePathsService,
    StudyHubDatabaseInitializer databaseInitializer,
    ILogger<AppBackupService> logger) : IAppBackupService
{
    private const string ManifestFileName = "backup-manifest.json";

    private readonly IStoragePathsService _storagePathsService = storagePathsService;
    private readonly StudyHubDatabaseInitializer _databaseInitializer = databaseInitializer;
    private readonly ILogger<AppBackupService> _logger = logger;

    public async Task<AppBackupDescriptor> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storagePathsService.EnsureStorageDirectories();

        var backupDirectory = _storagePathsService.CreateUniqueBackupDirectory("studyhub-backup");
        var databaseBackupDirectory = Path.Combine(backupDirectory, "database");
        var routineBackupDirectory = Path.Combine(backupDirectory, "routine");
        Directory.CreateDirectory(databaseBackupDirectory);
        Directory.CreateDirectory(routineBackupDirectory);

        _logger.LogInformation(
            "StudyHub app backup started. Reason: {Reason}. BackupDirectory: {BackupDirectory}",
            reason,
            backupDirectory);

        SqliteConnection.ClearAllPools();

        var databaseFiles = CopyDatabaseFiles(databaseBackupDirectory);
        var routineFiles = CopyRoutineFiles(routineBackupDirectory);

        var manifest = new BackupManifest
        {
            BackupId = Path.GetFileName(backupDirectory),
            BackupDirectory = backupDirectory,
            CreatedAtUtc = DateTime.UtcNow,
            Reason = reason ?? string.Empty,
            DatabaseFiles = databaseFiles,
            RoutineFiles = routineFiles
        };

        var manifestPath = Path.Combine(backupDirectory, ManifestFileName);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        _logger.LogInformation(
            "StudyHub app backup completed. BackupDirectory: {BackupDirectory}. DatabaseFiles: {DatabaseFiles}. RoutineFiles: {RoutineFiles}",
            backupDirectory,
            databaseFiles.Count,
            routineFiles.Count);

        return ToDescriptor(manifest, manifestPath);
    }

    public async Task<IReadOnlyList<AppBackupDescriptor>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _storagePathsService.EnsureStorageDirectories();

        var descriptors = new List<AppBackupDescriptor>();
        foreach (var directory in Directory.GetDirectories(_storagePathsService.BackupsDirectory)
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson);
            if (manifest == null)
            {
                continue;
            }

            descriptors.Add(ToDescriptor(manifest, manifestPath));
        }

        return descriptors;
    }

    public async Task<AppRestoreResult> RestoreBackupAsync(
        string backupDirectory,
        bool createSafetyBackup = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedBackupDirectory = NormalizeBackupDirectory(backupDirectory);
            var manifest = await LoadManifestAsync(normalizedBackupDirectory, cancellationToken);
            if (manifest == null)
            {
                return new AppRestoreResult
                {
                    Success = false,
                    BackupDirectory = normalizedBackupDirectory,
                    Message = "O backup informado nao possui manifest valido."
                };
            }

            _logger.LogInformation(
                "StudyHub app restore started. BackupDirectory: {BackupDirectory}. CreateSafetyBackup: {CreateSafetyBackup}",
                normalizedBackupDirectory,
                createSafetyBackup);

            string? safetyBackupDirectory = null;
            if (createSafetyBackup)
            {
                var safetyBackup = await CreateBackupAsync("pre-restore-safety-backup", cancellationToken);
                safetyBackupDirectory = safetyBackup.BackupDirectory;
            }

            SqliteConnection.ClearAllPools();
            DeleteCurrentDatabaseFiles();
            DeleteCurrentRoutineFiles();

            var restoredDatabase = RestoreDatabaseFiles(normalizedBackupDirectory);
            var restoredRoutine = RestoreRoutineFiles(normalizedBackupDirectory);

            _storagePathsService.EnsureStorageDirectories();
            await _databaseInitializer.InitializeAsync();

            _logger.LogInformation(
                "StudyHub app restore completed. BackupDirectory: {BackupDirectory}. RestoredDatabase: {RestoredDatabase}. RestoredRoutine: {RestoredRoutine}. SafetyBackup: {SafetyBackup}",
                normalizedBackupDirectory,
                restoredDatabase,
                restoredRoutine,
                safetyBackupDirectory ?? "<none>");

            return new AppRestoreResult
            {
                Success = true,
                BackupDirectory = normalizedBackupDirectory,
                SafetyBackupDirectory = safetyBackupDirectory,
                DatabaseRestored = restoredDatabase,
                RoutineRestored = restoredRoutine,
                Message = "Backup restaurado com sucesso. Se a interface estiver aberta, reinicie o app para recarregar todo o estado em memoria."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StudyHub app restore failed. BackupDirectory: {BackupDirectory}", backupDirectory);
            return new AppRestoreResult
            {
                Success = false,
                BackupDirectory = backupDirectory,
                Message = ex.Message
            };
        }
    }

    public async Task<AppResetResult> ResetAppStateAsync(bool createSafetyBackup = true, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storagePathsService.EnsureStorageDirectories();

            _logger.LogWarning(
                "StudyHub app reset started. CreateSafetyBackup: {CreateSafetyBackup}. DatabasePath: {DatabasePath}. RoutineDirectory: {RoutineDirectory}",
                createSafetyBackup,
                _storagePathsService.DatabasePath,
                _storagePathsService.RoutineDirectory);

            string? safetyBackupDirectory = null;
            if (createSafetyBackup)
            {
                var safetyBackup = await CreateBackupAsync("pre-reset-safety-backup", cancellationToken);
                safetyBackupDirectory = safetyBackup.BackupDirectory;
            }

            SqliteConnection.ClearAllPools();
            DeleteCurrentDatabaseFiles();
            DeleteCurrentRoutineFiles();

            _storagePathsService.EnsureStorageDirectories();
            await _databaseInitializer.InitializeAsync();

            _logger.LogWarning(
                "StudyHub app reset completed. SafetyBackup: {SafetyBackup}",
                safetyBackupDirectory ?? "<none>");

            return new AppResetResult
            {
                Success = true,
                SafetyBackupDirectory = safetyBackupDirectory,
                DatabaseReset = true,
                RoutineReset = true,
                Message = "Estado local do app resetado com sucesso. Os arquivos fisicos dos cursos do usuario nao foram alterados."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StudyHub app reset failed.");
            return new AppResetResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private List<string> CopyDatabaseFiles(string destinationDirectory)
    {
        var copiedFiles = new List<string>();

        foreach (var sourcePath in EnumerateDatabaseFiles())
        {
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            ValidateManagedPath(sourcePath);
            ValidateManagedPath(destinationPath);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            copiedFiles.Add(destinationPath);
        }

        return copiedFiles;
    }

    private List<string> CopyRoutineFiles(string destinationDirectory)
    {
        var copiedFiles = new List<string>();
        if (!Directory.Exists(_storagePathsService.RoutineDirectory))
        {
            return copiedFiles;
        }

        foreach (var sourceFile in Directory.GetFiles(_storagePathsService.RoutineDirectory, "*", SearchOption.AllDirectories))
        {
            ValidateManagedPath(sourceFile);
            var relativePath = Path.GetRelativePath(_storagePathsService.RoutineDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            ValidateManagedPath(destinationFile);

            var destinationParent = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(sourceFile, destinationFile, overwrite: false);
            copiedFiles.Add(destinationFile);
        }

        return copiedFiles;
    }

    private bool RestoreDatabaseFiles(string backupDirectory)
    {
        var sourceDirectory = Path.Combine(backupDirectory, "database");
        if (!Directory.Exists(sourceDirectory))
        {
            return false;
        }

        var restored = false;
        foreach (var sourceFile in Directory.GetFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(_storagePathsService.DatabaseDirectory, Path.GetFileName(sourceFile));
            ValidateManagedPath(destinationFile);
            File.Copy(sourceFile, destinationFile, overwrite: false);
            restored = true;
        }

        return restored;
    }

    private bool RestoreRoutineFiles(string backupDirectory)
    {
        var sourceDirectory = Path.Combine(backupDirectory, "routine");
        if (!Directory.Exists(sourceDirectory))
        {
            return false;
        }

        var restored = false;
        foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(_storagePathsService.RoutineDirectory, relativePath);
            ValidateManagedPath(destinationFile);

            var destinationParent = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(sourceFile, destinationFile, overwrite: false);
            restored = true;
        }

        return restored;
    }

    private void DeleteCurrentDatabaseFiles()
    {
        foreach (var filePath in EnumerateDatabaseFiles())
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            ValidateManagedPath(filePath);
            File.Delete(filePath);
        }
    }

    private void DeleteCurrentRoutineFiles()
    {
        if (!Directory.Exists(_storagePathsService.RoutineDirectory))
        {
            return;
        }

        ValidateManagedPath(_storagePathsService.RoutineDirectory);
        Directory.Delete(_storagePathsService.RoutineDirectory, recursive: true);
    }

    private IEnumerable<string> EnumerateDatabaseFiles()
    {
        yield return _storagePathsService.DatabasePath;
        yield return _storagePathsService.DatabasePath + "-wal";
        yield return _storagePathsService.DatabasePath + "-shm";
        yield return _storagePathsService.DatabasePath + "-journal";
    }

    private string NormalizeBackupDirectory(string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new InvalidOperationException("A backup directory must be provided for restore.");
        }

        var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
        if (!_storagePathsService.IsBackupPath(normalizedBackupDirectory))
        {
            throw new InvalidOperationException("The requested backup directory is outside the managed StudyHub backup area.");
        }

        if (!Directory.Exists(normalizedBackupDirectory))
        {
            throw new DirectoryNotFoundException($"The backup directory was not found: {normalizedBackupDirectory}");
        }

        return normalizedBackupDirectory;
    }

    private void ValidateManagedPath(string path)
    {
        if (!_storagePathsService.IsManagedPath(path) && !_storagePathsService.IsBackupPath(path))
        {
            throw new InvalidOperationException($"The requested path is outside the managed StudyHub storage roots: {path}");
        }
    }

    private static AppBackupDescriptor ToDescriptor(BackupManifest manifest, string manifestPath)
    {
        return new AppBackupDescriptor
        {
            BackupId = manifest.BackupId,
            BackupDirectory = manifest.BackupDirectory,
            ManifestPath = manifestPath,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Reason = manifest.Reason,
            DatabaseFiles = manifest.DatabaseFiles,
            RoutineFiles = manifest.RoutineFiles
        };
    }

    private static async Task<BackupManifest?> LoadManifestAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(backupDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize<BackupManifest>(manifestJson);
    }

    private sealed class BackupManifest
    {
        public string BackupId { get; set; } = string.Empty;
        public string BackupDirectory { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string> DatabaseFiles { get; set; } = [];
        public List<string> RoutineFiles { get; set; } = [];
    }
}
