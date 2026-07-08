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
}
