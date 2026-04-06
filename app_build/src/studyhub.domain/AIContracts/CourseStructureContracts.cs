namespace studyhub.domain.AIContracts;

public class CourseStructureContract
{
    public string SourceType { get; set; } = string.Empty;
    public string RawCourseTitle { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public List<ModuleStructureContract> DetectedModules { get; set; } = new();
}

public class ModuleStructureContract
{
    public string RawTitle { get; set; } = string.Empty;
    public string RawPath { get; set; } = string.Empty;
    public List<LessonStructureContract> Lessons { get; set; } = new();
}

public class LessonStructureContract
{
    public string RawTitle { get; set; } = string.Empty;
    public string RawFileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
}
