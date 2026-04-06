namespace studyhub.domain.AIContracts;

public class CoursePresentationRequestContract
{
    public CourseContextContract CourseContext { get; set; } = new();
    public string Goal { get; set; } = string.Empty;
}

public class CourseContextContract
{
    public string SourceType { get; set; } = string.Empty;
    public string RawCourseTitle { get; set; } = string.Empty;
    public int ModuleCount { get; set; }
    public int LessonCount { get; set; }
    public List<ModuleContextContract> DetectedModules { get; set; } = new();
}

public class ModuleContextContract
{
    public string RawTitle { get; set; } = string.Empty;
    public int LessonCount { get; set; }
}

public class CoursePresentationResponseContract
{
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseDescription { get; set; } = string.Empty;
    public List<DisplayModuleContract> DisplayModules { get; set; } = new();
}

public class DisplayModuleContract
{
    public string RawTitle { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
}
