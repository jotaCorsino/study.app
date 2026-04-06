namespace studyhub.domain.AIContracts;

public class CourseRoadmapRequestContract
{
    public RoadmapCourseInformationContract CourseInformation { get; set; } = new();
    public string Goal { get; set; } = string.Empty;
}

public class RoadmapCourseInformationContract
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int ModuleCount { get; set; }
    public int LessonCount { get; set; }
    public List<RoadmapModuleContract> Modules { get; set; } = new();
}

public class RoadmapModuleContract
{
    public string Title { get; set; } = string.Empty;
    public int TopicCount { get; set; }
    public int LessonCount { get; set; }
}

public class CourseRoadmapResponseContract
{
    public List<RoadmapLevelContract> Levels { get; set; } = new();
}

public class RoadmapLevelContract
{
    public int Order { get; set; }
    public string Kicker { get; set; } = string.Empty; // e.g. "Fundação técnica"
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string DetailedGoal { get; set; } = string.Empty;
    public List<string> FocusTags { get; set; } = new();
    public List<RoadmapStageContract> Stages { get; set; } = new();
}

public class RoadmapStageContract
{
    public int Order { get; set; }
    public string Kicker { get; set; } = string.Empty; // e.g. "Etapa 1"
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    
    public List<RoadmapChecklistBlockContract> Blocks { get; set; } = new();
    
    public string MasteryExpectation { get; set; } = string.Empty;
    public List<string> CommonMistakes { get; set; } = new();
    public List<string> ValidationQuestions { get; set; } = new();
}

public class RoadmapChecklistBlockContract
{
    public string Title { get; set; } = string.Empty; // e.g. "Base técnica" ou "Mini projeto"
    public string Description { get; set; } = string.Empty;
    public List<RoadmapChecklistItemContract> Items { get; set; } = new();
}

public class RoadmapChecklistItemContract
{
    public string Description { get; set; } = string.Empty;
}
