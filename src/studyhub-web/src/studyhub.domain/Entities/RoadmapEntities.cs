namespace studyhub.domain.Entities;

public class RoadmapLevel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public int Order { get; set; }
    public string Kicker { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string DetailedGoal { get; set; } = string.Empty;
    public List<string> FocusTags { get; set; } = new();
    
    public List<RoadmapStage> Stages { get; set; } = new();

    public int OverallProgress => Stages.Any() ? (int)Stages.Average(s => s.Progress) : 0;
}

public class RoadmapStage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public string Kicker { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    
    public List<RoadmapBlock> Blocks { get; set; } = new();
    
    public string MasteryExpectation { get; set; } = string.Empty;
    public List<string> CommonMistakes { get; set; } = new();
    public List<string> ValidationQuestions { get; set; } = new();

    public int Progress 
    {
        get
        {
            var totalItems = Blocks.SelectMany(b => b.Items).Count();
            if (totalItems == 0) return 0;
            var completedItems = Blocks.SelectMany(b => b.Items).Count(i => i.IsCompleted);
            return (int)((completedItems / (double)totalItems) * 100);
        }
    }
}

public class RoadmapBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<RoadmapChecklistItem> Items { get; set; } = new();
}

public class RoadmapChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
