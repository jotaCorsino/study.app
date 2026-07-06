using System.Text.Json;
using studyhub.domain.Entities;
using Xunit;

namespace studyhub.app.tests;

public sealed class RoutineModelSerializationTests
{
    [Fact]
    public void RoutineSettings_DeserializesLegacyJsonWithoutGoalModeUsingTimeMinutesDefaults()
    {
        var legacyJson = JsonSerializer.Serialize(new
        {
            DailyGoalMinutes = 45,
            SelectedDaysOfWeek = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday },
            LastUpdatedAt = new DateTime(2026, 7, 1),
            PlanPeriods = new[]
            {
                new
                {
                    StartDate = new DateTime(2026, 7, 1),
                    EndDate = (DateTime?)null,
                    DailyGoalMinutes = 45,
                    SelectedDaysOfWeek = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }
                }
            },
            SuspensionPeriods = Array.Empty<RoutineSuspensionPeriod>()
        });

        var settings = JsonSerializer.Deserialize<RoutineSettings>(legacyJson);

        Assert.NotNull(settings);
        Assert.Equal(DailyGoalMode.TimeMinutes, settings!.GoalMode);
        Assert.Equal(45, settings.DailyGoalMinutes);
        Assert.Equal(1, settings.DailyGoalStudyUnits);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, settings.SelectedDaysOfWeek);

        var plan = Assert.Single(settings.PlanPeriods);
        Assert.Equal(DailyGoalMode.TimeMinutes, plan.GoalMode);
        Assert.Equal(45, plan.DailyGoalMinutes);
        Assert.Equal(1, plan.DailyGoalStudyUnits);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, plan.SelectedDaysOfWeek);
    }

    [Fact]
    public void DailyStudyRecord_DeserializesLegacyJsonWithoutCompletedStudyUnitIdsAsEmptyCollection()
    {
        var courseId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var legacyJson = JsonSerializer.Serialize(new
        {
            CourseId = courseId,
            Date = new DateTime(2026, 7, 2),
            MinutesStudied = 30,
            NonLessonMinutesStudied = 0,
            LessonCredits = new[]
            {
                new
                {
                    LessonId = lessonId,
                    MinutesCredited = 30
                }
            },
            DailyGoalMinutesAtTheTime = 30,
            Status = DailyStudyStatus.Completed
        });

        var record = JsonSerializer.Deserialize<DailyStudyRecord>(legacyJson);

        Assert.NotNull(record);
        Assert.Equal(courseId, record!.CourseId);
        Assert.Equal(30, record.MinutesStudied);
        Assert.NotNull(record.CompletedStudyUnitIds);
        Assert.Empty(record.CompletedStudyUnitIds);
        Assert.Equal(0, record.CompletedStudyUnitCount);
        var credit = Assert.Single(record.LessonCredits);
        Assert.Equal(lessonId, credit.LessonId);
        Assert.Equal(30, credit.MinutesCredited);
    }

    [Fact]
    public void DailyStudyRecord_DeserializesEmptyCompletedStudyUnitIdsAsEmptyCollection()
    {
        var json = JsonSerializer.Serialize(new
        {
            CourseId = Guid.NewGuid(),
            Date = new DateTime(2026, 7, 2),
            MinutesStudied = 0,
            NonLessonMinutesStudied = 0,
            LessonCredits = Array.Empty<LessonStudyCredit>(),
            CompletedStudyUnitIds = Array.Empty<Guid>(),
            DailyGoalMinutesAtTheTime = 0,
            Status = DailyStudyStatus.NotStarted
        });

        var record = JsonSerializer.Deserialize<DailyStudyRecord>(json);

        Assert.NotNull(record);
        Assert.NotNull(record!.CompletedStudyUnitIds);
        Assert.Empty(record.CompletedStudyUnitIds);
        Assert.Equal(0, record.CompletedStudyUnitCount);
    }

    [Fact]
    public void DailyStudyRecord_DeserializesNullCompletedStudyUnitIdsAsEmptyCollection()
    {
        var json = $$"""
        {
          "CourseId": "{{Guid.NewGuid()}}",
          "Date": "2026-07-02T00:00:00",
          "MinutesStudied": 0,
          "NonLessonMinutesStudied": 0,
          "LessonCredits": [],
          "CompletedStudyUnitIds": null,
          "DailyGoalMinutesAtTheTime": 0,
          "Status": 0
        }
        """;

        var record = JsonSerializer.Deserialize<DailyStudyRecord>(json);

        Assert.NotNull(record);
        Assert.NotNull(record!.CompletedStudyUnitIds);
        Assert.Empty(record.CompletedStudyUnitIds);
        Assert.Equal(0, record.CompletedStudyUnitCount);
    }

    [Fact]
    public void RoutineSettings_RoundTripsStudyUnitsMode()
    {
        var settings = new RoutineSettings
        {
            GoalMode = DailyGoalMode.StudyUnits,
            DailyGoalMinutes = 90,
            DailyGoalStudyUnits = 2,
            SelectedDaysOfWeek = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            LastUpdatedAt = new DateTime(2026, 7, 3),
            PlanPeriods =
            [
                new RoutinePlanPeriod
                {
                    StartDate = new DateTime(2026, 7, 3),
                    GoalMode = DailyGoalMode.StudyUnits,
                    DailyGoalMinutes = 90,
                    DailyGoalStudyUnits = 2,
                    SelectedDaysOfWeek = [DayOfWeek.Tuesday, DayOfWeek.Thursday]
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings);
        var roundTripped = JsonSerializer.Deserialize<RoutineSettings>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(DailyGoalMode.StudyUnits, roundTripped!.GoalMode);
        Assert.Equal(90, roundTripped.DailyGoalMinutes);
        Assert.Equal(2, roundTripped.DailyGoalStudyUnits);
        Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday }, roundTripped.SelectedDaysOfWeek);

        var plan = Assert.Single(roundTripped.PlanPeriods);
        Assert.Equal(DailyGoalMode.StudyUnits, plan.GoalMode);
        Assert.Equal(90, plan.DailyGoalMinutes);
        Assert.Equal(2, plan.DailyGoalStudyUnits);
        Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday }, plan.SelectedDaysOfWeek);
    }

    [Fact]
    public void DailyStudyRecord_DeserializesValidCompletedStudyUnitIdsPreservingIds()
    {
        var firstStudyUnitId = Guid.NewGuid();
        var secondStudyUnitId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            CourseId = Guid.NewGuid(),
            Date = new DateTime(2026, 7, 4),
            MinutesStudied = 60,
            NonLessonMinutesStudied = 0,
            LessonCredits = Array.Empty<LessonStudyCredit>(),
            CompletedStudyUnitIds = new[] { firstStudyUnitId, secondStudyUnitId },
            DailyGoalMinutesAtTheTime = 60,
            Status = DailyStudyStatus.Completed
        });

        var record = JsonSerializer.Deserialize<DailyStudyRecord>(json);

        Assert.NotNull(record);
        Assert.NotNull(record!.CompletedStudyUnitIds);
        Assert.Equal(new[] { firstStudyUnitId, secondStudyUnitId }, record.CompletedStudyUnitIds);
        Assert.Equal(2, record.CompletedStudyUnitCount);
    }

    [Fact]
    public void DailyStudyRecord_RoundTripsCompletedStudyUnitIds()
    {
        var firstStudyUnitId = Guid.NewGuid();
        var secondStudyUnitId = Guid.NewGuid();
        var record = new DailyStudyRecord
        {
            CourseId = Guid.NewGuid(),
            Date = new DateTime(2026, 7, 4),
            CompletedStudyUnitIds = [firstStudyUnitId, secondStudyUnitId],
            DailyGoalMinutesAtTheTime = 60,
            MinutesStudied = 60,
            Status = DailyStudyStatus.Completed
        };

        var json = JsonSerializer.Serialize(record);
        var roundTripped = JsonSerializer.Deserialize<DailyStudyRecord>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(new[] { firstStudyUnitId, secondStudyUnitId }, roundTripped!.CompletedStudyUnitIds);
        Assert.Equal(2, roundTripped.CompletedStudyUnitCount);
        Assert.Equal(60, roundTripped.DailyGoalMinutesAtTheTime);
        Assert.Equal(60, roundTripped.MinutesStudied);
    }

    [Fact]
    public void DailyGoalEvaluation_DeserializesLegacyContractPreservingExistingFieldsAndSafeNewDefaults()
    {
        var courseId = Guid.NewGuid();
        var legacyJson = JsonSerializer.Serialize(new
        {
            CourseId = courseId,
            Date = new DateTime(2026, 7, 5),
            RawStatus = DailyStudyStatus.AlmostCompleted,
            MinutesStudied = 45,
            DailyGoalMinutesAtTheTime = 60,
            ExtraMinutes = 0,
            MissingMinutes = 15,
            ConsumedMonthlyCreditMinutes = 5,
            AvailableMonthlyCreditMinutes = 20,
            IsMonthlyCreditApplied = true,
            RawCompliancePercentage = 75d,
            EffectiveCompliancePercentage = 100d,
            CountsAsEffectiveGoalMet = true,
            IsPlannedDay = true,
            IsFutureDay = false
        });

        var evaluation = JsonSerializer.Deserialize<DailyGoalEvaluation>(legacyJson);

        Assert.NotNull(evaluation);
        Assert.Equal(courseId, evaluation!.CourseId);
        Assert.Equal(new DateTime(2026, 7, 5), evaluation.Date);
        Assert.Equal(DailyStudyStatus.AlmostCompleted, evaluation.RawStatus);
        Assert.Equal(45, evaluation.MinutesStudied);
        Assert.Equal(60, evaluation.DailyGoalMinutesAtTheTime);
        Assert.Equal(0, evaluation.ExtraMinutes);
        Assert.Equal(15, evaluation.MissingMinutes);
        Assert.Equal(5, evaluation.ConsumedMonthlyCreditMinutes);
        Assert.Equal(20, evaluation.AvailableMonthlyCreditMinutes);
        Assert.True(evaluation.IsMonthlyCreditApplied);
        Assert.Equal(75d, evaluation.RawCompliancePercentage);
        Assert.Equal(100d, evaluation.EffectiveCompliancePercentage);
        Assert.True(evaluation.CountsAsEffectiveGoalMet);
        Assert.True(evaluation.IsPlannedDay);
        Assert.False(evaluation.IsFutureDay);
        Assert.Equal(DailyGoalMode.TimeMinutes, evaluation.GoalMode);
        Assert.Equal(0, evaluation.GoalValueAtTheTime);
        Assert.Equal(0, evaluation.CompletedGoalValue);
        Assert.Equal("minutes", evaluation.GoalUnit);
    }
}
