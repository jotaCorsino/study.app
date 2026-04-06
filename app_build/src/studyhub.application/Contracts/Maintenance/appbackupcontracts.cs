namespace studyhub.application.Contracts.Maintenance;

public sealed class AppBackupDescriptor
{
    public string BackupId { get; set; } = string.Empty;
    public string BackupDirectory { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public IReadOnlyList<string> DatabaseFiles { get; set; } = [];
    public IReadOnlyList<string> RoutineFiles { get; set; } = [];
}

public sealed class AppRestoreResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string BackupDirectory { get; set; } = string.Empty;
    public string? SafetyBackupDirectory { get; set; }
    public bool DatabaseRestored { get; set; }
    public bool RoutineRestored { get; set; }
}

public sealed class AppResetResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SafetyBackupDirectory { get; set; }
    public bool DatabaseReset { get; set; }
    public bool RoutineReset { get; set; }
}
