using studyhub.app.Components.Routine;
using studyhub.domain.Entities;
using Xunit;

namespace studyhub.app.tests;

public sealed class RoutineGoalDisplayFormatterTests
{
    [Theory]
    [InlineData(30, "30m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    public void FormatGoal_UsesMinutesForTimeMode(int minutes, string expectedText)
    {
        var settings = new RoutineSettings
        {
            GoalMode = DailyGoalMode.TimeMinutes,
            DailyGoalMinutes = minutes,
            DailyGoalStudyUnits = 2
        };

        var text = RoutineGoalDisplayFormatter.FormatGoal(settings);

        Assert.Equal(expectedText, text);
    }

    [Theory]
    [InlineData(1, "1 Aula/Módulo")]
    [InlineData(2, "2 Aulas/Módulos")]
    public void FormatGoal_UsesStudyUnitsForStudyUnitsMode(int studyUnits, string expectedText)
    {
        var settings = new RoutineSettings
        {
            GoalMode = DailyGoalMode.StudyUnits,
            DailyGoalMinutes = 90,
            DailyGoalStudyUnits = studyUnits
        };

        var text = RoutineGoalDisplayFormatter.FormatGoal(settings);

        Assert.Equal(expectedText, text);
    }

    [Fact]
    public void FormatTodayProgress_UsesMinutesForTimeMode()
    {
        var settings = new RoutineSettings
        {
            GoalMode = DailyGoalMode.TimeMinutes,
            DailyGoalMinutes = 90,
            DailyGoalStudyUnits = 1
        };
        var record = new DailyStudyRecord
        {
            MinutesStudied = 75,
            CompletedStudyUnitIds = [Guid.NewGuid()]
        };

        var text = RoutineGoalDisplayFormatter.FormatTodayProgress(record, settings);

        Assert.Equal("1h 15m", text);
    }

    [Theory]
    [InlineData(0, 1, "0/1 Aula/Módulo")]
    [InlineData(1, 1, "1/1 Aula/Módulo")]
    [InlineData(1, 2, "1/2 Aulas/Módulos")]
    [InlineData(2, 2, "2/2 Aulas/Módulos")]
    public void FormatTodayProgress_UsesStudyUnitsForStudyUnitsMode(
        int completedStudyUnits,
        int dailyGoalStudyUnits,
        string expectedText)
    {
        var completedIds = Enumerable
            .Range(0, completedStudyUnits)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var settings = new RoutineSettings
        {
            GoalMode = DailyGoalMode.StudyUnits,
            DailyGoalMinutes = 90,
            DailyGoalStudyUnits = dailyGoalStudyUnits
        };
        var record = new DailyStudyRecord
        {
            MinutesStudied = 120,
            CompletedStudyUnitIds = completedIds
        };

        var text = RoutineGoalDisplayFormatter.FormatTodayProgress(record, settings);

        Assert.Equal(expectedText, text);
    }

    [Theory]
    [InlineData(1, "Aula/Módulo")]
    [InlineData(2, "Aulas/Módulos")]
    public void FormatStudyUnitLabel_PluralizesStudyUnits(int studyUnits, string expectedText)
    {
        var text = RoutineGoalDisplayFormatter.FormatStudyUnitLabel(studyUnits);

        Assert.Equal(expectedText, text);
    }

    [Fact]
    public void FormatCalendarDayProgress_UsesTimeMinutes()
    {
        var evaluation = CreateEvaluation(
            DailyGoalMode.TimeMinutes,
            completedGoalValue: 45,
            goalValueAtTheTime: 90,
            minutesStudied: 45);

        var text = RoutineGoalDisplayFormatter.FormatCalendarDayProgress(evaluation);

        Assert.Equal("45m / 1h 30m", text);
    }

    [Theory]
    [InlineData(0, 1, "0/1 Aula/Módulo")]
    [InlineData(2, 2, "2/2 Aulas/Módulos")]
    public void FormatCalendarDayProgress_UsesStudyUnits(
        int completedStudyUnits,
        int goalStudyUnits,
        string expectedText)
    {
        var evaluation = CreateEvaluation(
            DailyGoalMode.StudyUnits,
            completedStudyUnits,
            goalStudyUnits);

        var text = RoutineGoalDisplayFormatter.FormatCalendarDayProgress(evaluation);

        Assert.Equal(expectedText, text);
    }

    [Fact]
    public void FormatNavMenuIndicator_UsesTimeMinutes()
    {
        var evaluation = CreateEvaluation(
            DailyGoalMode.TimeMinutes,
            completedGoalValue: 45,
            goalValueAtTheTime: 90,
            minutesStudied: 45);

        var text = RoutineGoalDisplayFormatter.FormatNavMenuIndicator(evaluation);

        Assert.Equal("Hoje: 45m / 1h 30m", text);
    }

    [Fact]
    public void FormatNavMenuIndicator_UsesStudyUnits()
    {
        var evaluation = CreateEvaluation(
            DailyGoalMode.StudyUnits,
            completedGoalValue: 2,
            goalValueAtTheTime: 2);

        var text = RoutineGoalDisplayFormatter.FormatNavMenuIndicator(evaluation);

        Assert.Equal("Hoje: 2/2 Aulas/Módulos", text);
    }

    [Fact]
    public void FormatCalendarDayProgress_AllowsMixedModeEvaluations()
    {
        var evaluations = new[]
        {
            CreateEvaluation(DailyGoalMode.TimeMinutes, completedGoalValue: 30, goalValueAtTheTime: 60, minutesStudied: 30),
            CreateEvaluation(DailyGoalMode.StudyUnits, completedGoalValue: 1, goalValueAtTheTime: 1)
        };

        var texts = evaluations
            .Select(RoutineGoalDisplayFormatter.FormatCalendarDayProgress)
            .ToArray();

        Assert.Equal(["30m / 1h", "1/1 Aula/Módulo"], texts);
    }

    [Fact]
    public void FormatterOutput_DoesNotExposeInternalStudyUnitsTerm()
    {
        var studyUnitsEvaluation = CreateEvaluation(
            DailyGoalMode.StudyUnits,
            completedGoalValue: 1,
            goalValueAtTheTime: 2);
        var settings = new RoutineSettings
        {
            GoalMode = DailyGoalMode.StudyUnits,
            DailyGoalStudyUnits = 2
        };
        var record = new DailyStudyRecord
        {
            CompletedStudyUnitIds = [Guid.NewGuid()]
        };
        var texts = new[]
        {
            RoutineGoalDisplayFormatter.FormatGoal(settings),
            RoutineGoalDisplayFormatter.FormatTodayProgress(record, settings),
            RoutineGoalDisplayFormatter.FormatCalendarDayProgress(studyUnitsEvaluation),
            RoutineGoalDisplayFormatter.FormatNavMenuIndicator(studyUnitsEvaluation),
            RoutineGoalDisplayFormatter.FormatStudyUnitCount(0)
        };

        Assert.All(texts, text => Assert.DoesNotContain("StudyUnits", text, StringComparison.OrdinalIgnoreCase));
    }

    private static DailyGoalEvaluation CreateEvaluation(
        DailyGoalMode goalMode,
        int completedGoalValue,
        int goalValueAtTheTime,
        int minutesStudied = 0)
    {
        var percentage = goalValueAtTheTime > 0
            ? Math.Min(100d, completedGoalValue * 100d / goalValueAtTheTime)
            : 0d;

        return new DailyGoalEvaluation
        {
            GoalMode = goalMode,
            GoalValueAtTheTime = goalValueAtTheTime,
            CompletedGoalValue = completedGoalValue,
            GoalUnit = goalMode == DailyGoalMode.StudyUnits ? "study-units" : "minutes",
            MinutesStudied = minutesStudied,
            DailyGoalMinutesAtTheTime = goalMode == DailyGoalMode.TimeMinutes ? goalValueAtTheTime : 0,
            RawCompliancePercentage = percentage,
            EffectiveCompliancePercentage = percentage,
            CountsAsEffectiveGoalMet = completedGoalValue >= goalValueAtTheTime,
            IsPlannedDay = true
        };
    }
}
