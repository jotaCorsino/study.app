using studyhub.application.Contracts.Maintenance;

namespace studyhub.application.Interfaces;

public interface IAppBackupService
{
    Task<AppBackupDescriptor> CreateBackupAsync(string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppBackupDescriptor>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<AppRestoreResult> RestoreBackupAsync(string backupDirectory, bool createSafetyBackup = true, CancellationToken cancellationToken = default);
    Task<AppResetResult> ResetAppStateAsync(bool createSafetyBackup = true, CancellationToken cancellationToken = default);
}
