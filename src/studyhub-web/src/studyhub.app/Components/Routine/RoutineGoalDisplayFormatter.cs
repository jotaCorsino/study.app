using studyhub.domain.Entities;

namespace studyhub.app.Components.Routine;

public static class RoutineGoalDisplayFormatter
{
    public static string FormatGoal(RoutineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.GoalMode == DailyGoalMode.StudyUnits
            ? FormatStudyUnitGoal(settings.DailyGoalStudyUnits)
            : FormatMinutes(settings.DailyGoalMinutes);
    }

    public static string FormatTodayProgress(DailyStudyRecord? record, RoutineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.GoalMode == DailyGoalMode.StudyUnits)
        {
            var completedStudyUnits = Math.Max(0, record?.CompletedStudyUnitCount ?? 0);
            var dailyGoalStudyUnits = Math.Max(1, settings.DailyGoalStudyUnits);
            return FormatStudyUnitProgress(completedStudyUnits, dailyGoalStudyUnits);
        }

        return FormatMinutes(record?.MinutesStudied ?? 0);
    }

    public static string FormatEvaluationProgress(DailyGoalEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return evaluation.GoalMode == DailyGoalMode.StudyUnits
            ? FormatStudyUnitProgress(evaluation.CompletedGoalValue, evaluation.GoalValueAtTheTime)
            : $"{evaluation.MinutesStudied} min (Meta: {evaluation.DailyGoalMinutesAtTheTime} min)";
    }

    public static string FormatStudyUnitProgress(int completedStudyUnits, int dailyGoalStudyUnits)
    {
        var completed = Math.Max(0, completedStudyUnits);
        var goal = Math.Max(1, dailyGoalStudyUnits);
        return $"{completed}/{goal} {FormatStudyUnitLabel(goal)}";
    }

    public static string FormatStudyUnitGoal(int studyUnits)
    {
        var normalizedStudyUnits = Math.Max(1, studyUnits);
        return $"{normalizedStudyUnits} {FormatStudyUnitLabel(normalizedStudyUnits)}";
    }

    public static string FormatStudyUnitLabel(int studyUnits)
    {
        return Math.Max(1, studyUnits) == 1
            ? "Aula/Módulo"
            : "Aulas/Módulos";
    }

    public static string FormatMinutes(int minutes)
    {
        var normalizedMinutes = Math.Max(0, minutes);
        if (normalizedMinutes < 60)
        {
            return $"{normalizedMinutes}m";
        }

        var hours = normalizedMinutes / 60;
        var remainingMinutes = normalizedMinutes % 60;
        return remainingMinutes > 0 ? $"{hours}h {remainingMinutes}m" : $"{hours}h";
    }
}
