namespace studyhub.application.Contracts.CourseBuilding;

public sealed class OnlineCoursePlanningRequest
{
    public OnlineCourseIntentRequest Intent { get; set; } = new();
}

public sealed class OnlineCoursePlanningResponse
{
    public string FriendlyTitle { get; set; } = string.Empty;
    public string CourseDescription { get; set; } = string.Empty;
    public string PedagogicalDirection { get; set; } = string.Empty;
    public string RoadmapMacro { get; set; } = string.Empty;
    public List<string> DiscoveryQueries { get; set; } = [];
    public List<OnlineCourseModulePlan> Modules { get; set; } = [];
}

public sealed class OnlineCourseModulePlan
{
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SearchQueries { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
}
