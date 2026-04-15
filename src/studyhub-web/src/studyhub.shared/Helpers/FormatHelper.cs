namespace studyhub.shared.Helpers;

public static class FormatHelper
{
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}min";
        return $"{duration.Minutes}min";
    }

    public static string FormatPercentage(double value)
        => $"{value:F0}%";

    public static string FormatLessonCount(int count)
        => count == 1 ? "1 aula" : $"{count} aulas";

    public static string FormatModuleCount(int count)
        => count == 1 ? "1 módulo" : $"{count} módulos";
}
