namespace studyhub.application.Interfaces;

public interface IStoragePathsService
{
    string AppDataDirectory { get; }
    string DatabaseDirectory { get; }
    string DatabasePath { get; }
    string BackupsDirectory { get; }
    string RoutineDirectory { get; }

    void EnsureStorageDirectories();
    bool IsManagedPath(string path);
    bool IsBackupPath(string path);
    string CreateUniqueBackupDirectory(string prefix);
}
