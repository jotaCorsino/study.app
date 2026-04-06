using studyhub.application.Interfaces;

namespace studyhub.infrastructure.services;

public sealed class StoragePathsService(string databasePath) : IStoragePathsService
{
    public string DatabasePath { get; } = NormalizePath(databasePath);

    public string AppDataDirectory => DatabaseDirectory;

    public string DatabaseDirectory => Path.GetDirectoryName(DatabasePath)
        ?? throw new InvalidOperationException("The StudyHub database directory could not be resolved.");

    public string BackupsDirectory => Path.Combine(DatabaseDirectory, "backups");

    public string RoutineDirectory { get; } = NormalizePath(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudyHub",
            "Routine"));

    public void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(RoutineDirectory);
    }

    public bool IsManagedPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        return IsUnderRoot(normalizedPath, DatabaseDirectory) ||
               IsUnderRoot(normalizedPath, RoutineDirectory);
    }

    public bool IsBackupPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        return IsUnderRoot(normalizedPath, BackupsDirectory);
    }

    public string CreateUniqueBackupDirectory(string prefix)
    {
        EnsureStorageDirectories();

        var sanitizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? "studyhub-backup"
            : prefix.Trim().ToLowerInvariant();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var candidate = Path.Combine(BackupsDirectory, $"{sanitizedPrefix}-{timestamp}");
        var attempt = 1;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(BackupsDirectory, $"{sanitizedPrefix}-{timestamp}-{attempt++}");
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("A required StudyHub storage path is empty.");
        }

        return Path.GetFullPath(path);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedPath = NormalizeDirectorySeparators(NormalizePath(path));
        var normalizedRoot = NormalizeDirectorySeparators(NormalizePath(root));

        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedPath.TrimEnd(Path.DirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectorySeparators(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}
