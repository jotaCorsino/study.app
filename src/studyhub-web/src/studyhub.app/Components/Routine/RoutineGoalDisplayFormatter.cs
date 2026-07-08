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
        return FormatCalendarDayProgress(evaluation);
    }

    public static string FormatCalendarDayProgress(DailyGoalEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        if (!evaluation.IsPlannedDay || evaluation.GoalValueAtTheTime <= 0)
        {
            return "Não planejado";
        }

        return evaluation.GoalMode == DailyGoalMode.StudyUnits
            ? FormatStudyUnitProgress(evaluation.CompletedGoalValue, evaluation.GoalValueAtTheTime)
            : $"{FormatMinutes(evaluation.CompletedGoalValue)} / {FormatMinutes(evaluation.GoalValueAtTheTime)}";
    }

    public static string FormatNavMenuIndicator(DailyGoalEvaluation? evaluation)
    {
        if (evaluation is null || !evaluation.IsPlannedDay || evaluation.GoalValueAtTheTime <= 0)
        {
            return "Hoje: sem meta";
        }

        return $"Hoje: {FormatCalendarDayProgress(evaluation)}";
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

    public static string FormatStudyUnitCount(int studyUnits)
    {
        var normalizedStudyUnits = Math.Max(0, studyUnits);
        return $"{normalizedStudyUnits} {FormatStudyUnitLabel(normalizedStudyUnits)}";
    }

    public static string FormatStudyUnitLabel(int studyUnits)
    {
        return studyUnits == 1
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
