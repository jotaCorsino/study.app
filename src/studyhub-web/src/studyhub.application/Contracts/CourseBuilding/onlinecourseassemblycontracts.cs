using studyhub.application.Contracts.Integrations;
using studyhub.domain.Entities;

namespace studyhub.application.Contracts.CourseBuilding;

public sealed class OnlineCourseAssemblyRequest
{
    public OnlineCourseIntentRequest Intent { get; set; } = new();
    public OnlineCoursePlanningResponse Planning { get; set; } = new();
    public SourceCurationResponse Curation { get; set; } = new();
    public CourseTextRefinementResponse? TextRefinement { get; set; }
}

public sealed class OnlineCuratedCourseBuildResult
{
    public Course Course { get; set; } = new();
    public IReadOnlyList<CuratedCourseSourceReference> CuratedSources { get; set; } = [];
}
