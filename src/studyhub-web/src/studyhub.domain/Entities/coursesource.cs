namespace studyhub.domain.Entities;

public enum CourseSourceType
{
    LocalFolder = 0
}

public enum LessonSourceType
{
    LocalFile = 0
}

public class CourseSourceMetadata
{
    private int _introSkipSeconds;

    public string RootPath { get; set; } = string.Empty;
    public DateTime? ImportedAt { get; set; }
    public string ScanVersion { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public bool IntroSkipEnabled { get; set; }

    public int IntroSkipSeconds
    {
        get => _introSkipSeconds;
        set => _introSkipSeconds = value < 0 ? 0 : value;
    }
}
