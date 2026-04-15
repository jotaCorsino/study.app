namespace studyhub.application.Contracts.LocalImport;

public class DetectedCourseStructure
{
    public Guid CourseId { get; set; }
    public string RootFolderName { get; set; } = string.Empty;
    public string RootFolderPath { get; set; } = string.Empty;
    public string PresentationRootRelativePath { get; set; } = ".";
    public DateTime ScannedAt { get; set; }
    public DetectedFolderNode RootNode { get; set; } = new();
    public List<DetectedModuleStructure> Modules { get; set; } = [];

    public int TopicCount => Modules.Sum(module => module.Topics.Count);
    public int LessonCount => Modules.Sum(module => module.Topics.Sum(topic => topic.Lessons.Count));
}

public class DetectedFolderNode
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<DetectedFolderNode> Children { get; set; } = [];
    public List<DetectedLessonFile> DirectLessons { get; set; } = [];
}

public class DetectedModuleStructure
{
    public Guid ModuleId { get; set; }
    public int Order { get; set; }
    public string RawName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<DetectedTopicStructure> Topics { get; set; } = [];
}

public class DetectedTopicStructure
{
    public Guid TopicId { get; set; }
    public int Order { get; set; }
    public string RawName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<DetectedLessonFile> Lessons { get; set; } = [];
}

public class DetectedLessonFile
{
    public Guid LessonId { get; set; }
    public int Order { get; set; }
    public string RawName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public TimeSpan Duration { get; set; }
}
