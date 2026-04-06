namespace studyhub.application.Contracts.Integrations;

public sealed class CourseTextRefinementRequest
{
    public Guid CourseId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseDescription { get; set; } = string.Empty;
    public IReadOnlyList<ModuleTextRefinementInput> Modules { get; set; } = [];
}

public sealed class CourseTextRefinementResponse
{
    public string RefinedCourseTitle { get; set; } = string.Empty;
    public string RefinedCourseDescription { get; set; } = string.Empty;
    public IReadOnlyList<ModuleTextRefinementOutput> Modules { get; set; } = [];
}

public sealed class ModuleTextRefinementInput
{
    public Guid ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<LessonTextRefinementInput> Lessons { get; set; } = [];
}

public sealed class LessonTextRefinementInput
{
    public Guid LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ModuleTextRefinementOutput
{
    public Guid ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<LessonTextRefinementOutput> Lessons { get; set; } = [];
}

public sealed class LessonTextRefinementOutput
{
    public Guid LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
