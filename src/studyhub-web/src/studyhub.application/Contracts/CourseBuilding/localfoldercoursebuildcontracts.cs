using studyhub.application.Contracts.LocalImport;
using studyhub.domain.Entities;
using studyhub.shared.Enums;

namespace studyhub.application.Contracts.CourseBuilding;

public class LocalFolderCourseBuildRequest
{
    public string FolderPath { get; set; } = string.Empty;
    public DateTime? ExistingAddedAt { get; set; }
    public DateTime? ExistingLastAccessedAt { get; set; }
    public Dictionary<Guid, LocalFolderLessonStateSnapshot> ExistingLessonStates { get; set; } = [];
}

public class LocalFolderLessonStateSnapshot
{
    public LessonStatus Status { get; set; } = LessonStatus.NotStarted;
    public double WatchedPercentage { get; set; }
    public int DurationMinutes { get; set; }
    public int LastPlaybackPositionSeconds { get; set; }
}

public class LocalFolderCourseBuildResult
{
    public DetectedCourseStructure DetectedStructure { get; set; } = new();
    public Course Course { get; set; } = new();
}
