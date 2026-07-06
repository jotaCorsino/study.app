using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public class RoutineService : IRoutineService
{
    private const string MinutesGoalUnit = "minutes";
    private const string StudyUnitsGoalUnit = "study-units";

    private readonly string _baseDirectory;
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory;

    private readonly record struct DailyGoalSnapshot(DailyGoalMode GoalMode, int GoalValue);

    public RoutineService(IStoragePathsService storagePathsService, IDbContextFactory<StudyHubDbContext> contextFactory)
    {
        _baseDirectory = storagePathsService.RoutineDirectory;
        _contextFactory = contextFactory;
    }
    
    private string GetCourseDirectory(Guid courseId)
    {
        var dir = Path.Combine(_baseDirectory, courseId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string GetSettingsPath(Guid courseId) => Path.Combine(GetCourseDirectory(courseId), "routine_settings.json");
    private string GetRecordsPath(Guid courseId) => Path.Combine(GetCourseDirectory(courseId), "daily_records.json");

    public async Task<RoutineSettings> GetSettingsAsync(Guid courseId)
    {
        var path = GetSettingsPath(courseId);
        if (!File.Exists(path)) return new RoutineSettings();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var settings = JsonSerializer.Deserialize<RoutineSettings>(json) ?? new RoutineSettings();
            NormalizeSettings(settings, migrateLegacyPlanPeriods: true);
            return settings;
        }
        catch
        {
            return new RoutineSettings();
        }
    }

    public async Task SaveSettingsAsync(Guid courseId, RoutineSettings settings, DateTime? changedAt = null)
    {
        var changedAtValue = changedAt ?? DateTime.Now;
        var currentSettings = await GetSettingsAsync(courseId);
        var settingsToSave = new RoutineSettings
        {
            GoalMode = settings.GoalMode,
            DailyGoalMinutes = settings.DailyGoalMinutes,
            DailyGoalStudyUnits = settings.DailyGoalStudyUnits,
            SelectedDaysOfWeek = settings.SelectedDaysOfWeek?.ToList() ?? [],
            LastUpdatedAt = changedAtValue,
            PlanPeriods = currentSettings.PlanPeriods.Count > 0
                ? ClonePlanPeriods(currentSettings.PlanPeriods)
                : ClonePlanPeriods(settings.PlanPeriods),
            SuspensionPeriods = currentSettings.SuspensionPeriods.Count > 0
                ? CloneSuspensionPeriods(currentSettings.SuspensionPeriods)
                : CloneSuspensionPeriods(settings.SuspensionPeriods)
        };

        ApplyPlanChange(settingsToSave, changedAtValue.Date);
        settingsToSave.LastUpdatedAt = changedAtValue;
        await SaveSettingsFileAsync(courseId, settingsToSave, refreshLastUpdatedAt: false);
    }

    public async Task SuspendRoutineAsync(Guid courseId, RoutineSuspensionReason reason, DateTime? startDate = null)
    {
        if (courseId == Guid.Empty)
        {
            return;
        }

        var suspensionStartDate = (startDate ?? DateTime.Now).Date;
        var settings = await GetSettingsAsync(courseId);
        var openPeriod = settings.SuspensionPeriods
            .Where(period => !period.EndDate.HasValue)
            .OrderByDescending(period => period.StartDate)
            .FirstOrDefault();

        if (openPeriod == null)
        {
            settings.SuspensionPeriods.Add(new RoutineSuspensionPeriod
            {
                StartDate = suspensionStartDate,
                Reason = reason
            });
        }
        else
        {
            openPeriod.Reason = reason;
            if (suspensionStartDate < openPeriod.StartDate.Date)
            {
                openPeriod.StartDate = suspensionStartDate;
            }
        }

        await SaveSettingsFileAsync(courseId, settings, refreshLastUpdatedAt: false);
    }

    public async Task ReactivateRoutineAsync(Guid courseId, DateTime? reactivatedAt = null)
    {
        if (courseId == Guid.Empty)
        {
            return;
        }

        var reactivationDate = (reactivatedAt ?? DateTime.Now).Date;
        var lastSuspendedDate = reactivationDate.AddDays(-1);
        var settings = await GetSettingsAsync(courseId);
        var openPeriods = settings.SuspensionPeriods
            .Where(period => !period.EndDate.HasValue)
            .ToList();

        foreach (var period in openPeriods)
        {
            if (lastSuspendedDate < period.StartDate.Date)
            {
                settings.SuspensionPeriods.Remove(period);
                continue;
            }

            period.EndDate = lastSuspendedDate;
        }

        await SaveSettingsFileAsync(courseId, settings, refreshLastUpdatedAt: false);
    }

    private async Task<List<DailyStudyRecord>> GetAllRecordsAsync(Guid courseId)
    {
        var path = GetRecordsPath(courseId);
        if (!File.Exists(path)) return new List<DailyStudyRecord>();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<DailyStudyRecord>>(json) ?? new List<DailyStudyRecord>();
        }
        catch
        {
            return new List<DailyStudyRecord>();
        }
    }

    public async Task<DailyStudyRecord> GetDailyRecordAsync(Guid courseId, DateTime date)
    {
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        var record = allRecords.FirstOrDefault(r => r.Date.Date == date.Date) ?? new DailyStudyRecord
        {
            CourseId = courseId,
            Date = date.Date
        };

        NormalizeRecord(courseId, record);
        ApplyStatus(record, settings, effectiveStartDate);
        return record;
    }

    public async Task<IReadOnlyDictionary<Guid, DailyStudyRecord>> GetDailyRecordsAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime date)
    {
        if (courseIds.Count == 0)
        {
            return new Dictionary<Guid, DailyStudyRecord>();
        }

        var normalizedCourseIds = courseIds
            .Where(courseId => courseId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedCourseIds.Count == 0)
        {
            return new Dictionary<Guid, DailyStudyRecord>();
        }

        var addedAtByCourse = await GetCourseAddedAtMapAsync(normalizedCourseIds);
        var recordsByCourse = new Dictionary<Guid, DailyStudyRecord>(normalizedCourseIds.Count);
        var targetDate = date.Date;

        foreach (var courseId in normalizedCourseIds)
        {
            var allRecords = await GetAllRecordsAsync(courseId);
            var settings = await GetSettingsAsync(courseId);
            addedAtByCourse.TryGetValue(courseId, out var addedAt);
            var effectiveStartDate = ResolveEffectiveCourseStartDate(addedAt, allRecords, settings);
            var record = allRecords.FirstOrDefault(item => item.Date.Date == targetDate) ?? new DailyStudyRecord
            {
                CourseId = courseId,
                Date = targetDate
            };

            NormalizeRecord(courseId, record);
            ApplyStatus(record, settings, effectiveStartDate);
            recordsByCourse[courseId] = record;
        }

        return recordsByCourse;
    }

    public async Task<List<DailyStudyRecord>> GetMonthlyRecordsAsync(Guid courseId, int year, int month)
    {
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        var records = new List<DailyStudyRecord>();
        int daysInMonth = DateTime.DaysInMonth(year, month);

        for (int i = 1; i <= daysInMonth; i++)
        {
            var date = new DateTime(year, month, i);
            var record = allRecords.FirstOrDefault(r => r.Date.Date == date.Date) ?? new DailyStudyRecord
            {
                CourseId = courseId,
                Date = date.Date
            };
            NormalizeRecord(courseId, record);
            ApplyStatus(record, settings, effectiveStartDate);
            records.Add(record);
        }

        return records;
    }

    public async Task<List<DailyGoalEvaluation>> GetMonthlyGoalEvaluationsAsync(Guid courseId, int year, int month)
    {
        return await GetMonthlyGoalEvaluationsAsync(courseId, year, month, DateTime.Now.Date);
    }

    public async Task<List<DailyGoalEvaluation>> GetMonthlyGoalEvaluationsAsync(Guid courseId, int year, int month, DateTime today)
    {
        var records = await GetMonthlyRecordsAsync(courseId, year, month);
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        return BuildMonthlyGoalEvaluations(courseId, records, settings, effectiveStartDate, year, month, today.Date);
    }

    public async Task AddStudyTimeAsync(Guid courseId, int minutes)
    {
        if (minutes <= 0) return;

        var date = DateTime.Now.Date;
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);

        var recordItem = GetOrCreateRecord(allRecords, courseId, date);
        recordItem.NonLessonMinutesStudied += minutes;
        NormalizeRecord(courseId, recordItem);
        ApplyStatus(recordItem, settings, effectiveStartDate);

        await SaveAllRecordsAsync(courseId, allRecords);
    }

    public async Task CreditLessonProgressAsync(Guid courseId, Guid lessonId, int creditedMinutes, DateTime? date = null)
    {
        if (courseId == Guid.Empty || lessonId == Guid.Empty)
        {
            return;
        }

        var minutesToCredit = Math.Max(0, creditedMinutes);
        if (minutesToCredit <= 0)
        {
            return;
        }

        var studyDate = (date ?? DateTime.Now).Date;
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        var recordItem = GetOrCreateRecord(allRecords, courseId, studyDate);
        var lessonCredit = recordItem.LessonCredits.FirstOrDefault(credit => credit.LessonId == lessonId);
        if (lessonCredit == null)
        {
            lessonCredit = new LessonStudyCredit
            {
                LessonId = lessonId
            };
            recordItem.LessonCredits.Add(lessonCredit);
        }

        lessonCredit.MinutesCredited += minutesToCredit;
        NormalizeRecord(courseId, recordItem);
        ApplyStatus(recordItem, settings, effectiveStartDate);

        await SaveAllRecordsAsync(courseId, allRecords);
    }

    public async Task<bool> CreditStudyUnitProgressAsync(Guid courseId, Guid studyUnitId, DateTime? date = null)
    {
        if (courseId == Guid.Empty || studyUnitId == Guid.Empty)
        {
            return false;
        }

        var studyDate = (date ?? DateTime.Now).Date;
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        var recordItem = GetOrCreateRecord(allRecords, courseId, studyDate);
        var added = !recordItem.CompletedStudyUnitIds.Contains(studyUnitId);

        if (added)
        {
            recordItem.CompletedStudyUnitIds.Add(studyUnitId);
        }

        NormalizeRecord(courseId, recordItem);
        ApplyStatus(recordItem, settings, effectiveStartDate);

        await SaveAllRecordsAsync(courseId, allRecords);
        return added;
    }

    public async Task<int> GetCurrentStreakAsync(Guid courseId, DateTime? referenceDate = null)
    {
        var settings = await GetSettingsAsync(courseId);
        var allRecords = await GetAllRecordsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        var recordsByDate = allRecords
            .Select(record =>
            {
                NormalizeRecord(courseId, record);
                ApplyStatus(record, settings, effectiveStartDate);
                return record;
            })
            .GroupBy(record => record.Date.Date)
            .ToDictionary(group => group.Key, group => group.First());
        var studiedDates = recordsByDate.Values
            .Where(record => record.Status != DailyStudyStatus.Unplanned && HasStudyActivity(record))
            .Select(record => record.Date.Date)
            .ToHashSet();

        if (studiedDates.Count == 0)
        {
            return 0;
        }

        var today = (referenceDate ?? DateTime.Now).Date;
        var earliestStudiedDate = studiedDates.Min();
        var streak = 0;
        var cursor = studiedDates.Contains(today) ? today : today.AddDays(-1);

        while (cursor >= earliestStudiedDate)
        {
            var record = ResolveRecordForStreak(courseId, cursor, recordsByDate, settings, effectiveStartDate);
            if (record.Status == DailyStudyStatus.Unplanned)
            {
                cursor = cursor.AddDays(-1);
                continue;
            }

            if (!studiedDates.Contains(cursor))
            {
                break;
            }

            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private async Task SaveAllRecordsAsync(Guid courseId, List<DailyStudyRecord> allRecords)
    {
        var settings = await GetSettingsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        foreach (var record in allRecords)
        {
            NormalizeRecord(courseId, record);
            ApplyStatus(record, settings, effectiveStartDate);
        }

        var json = JsonSerializer.Serialize(allRecords.OrderBy(record => record.Date).ToList(), new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetRecordsPath(courseId), json);
    }

    private async Task SaveSettingsFileAsync(Guid courseId, RoutineSettings settings, bool refreshLastUpdatedAt)
    {
        NormalizeSettings(settings);

        if (refreshLastUpdatedAt)
        {
            settings.LastUpdatedAt = DateTime.Now;
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetSettingsPath(courseId), json);
    }

    private static void ApplyPlanChange(RoutineSettings settings, DateTime effectiveDate)
    {
        NormalizeSettings(settings);
        var hasValidPlan = HasValidCurrentPlan(settings);
        var openPlan = settings.PlanPeriods
            .Where(period => !period.EndDate.HasValue)
            .OrderByDescending(period => period.StartDate)
            .FirstOrDefault();

        if (openPlan != null && hasValidPlan && PlanMatches(openPlan, settings))
        {
            return;
        }

        if (openPlan == null)
        {
            if (hasValidPlan)
            {
                settings.PlanPeriods.Add(CreatePlanPeriod(settings, effectiveDate));
            }

            NormalizeSettings(settings);
            return;
        }

        if (effectiveDate <= openPlan.StartDate.Date)
        {
            settings.PlanPeriods.RemoveAll(period => period.StartDate.Date >= effectiveDate);
            if (hasValidPlan)
            {
                settings.PlanPeriods.Add(CreatePlanPeriod(settings, effectiveDate));
            }

            NormalizeSettings(settings);
            return;
        }

        openPlan.EndDate = effectiveDate.AddDays(-1);
        if (hasValidPlan)
        {
            settings.PlanPeriods.Add(CreatePlanPeriod(settings, effectiveDate));
        }

        NormalizeSettings(settings);
    }

    private static bool HasValidCurrentPlan(RoutineSettings settings)
    {
        return settings.SelectedDaysOfWeek.Count > 0 && HasValidGoal(settings.GoalMode, settings.DailyGoalMinutes, settings.DailyGoalStudyUnits);
    }

    private static RoutinePlanPeriod CreatePlanPeriod(RoutineSettings settings, DateTime startDate)
    {
        return new RoutinePlanPeriod
        {
            StartDate = startDate.Date,
            GoalMode = settings.GoalMode,
            DailyGoalMinutes = settings.DailyGoalMinutes,
            DailyGoalStudyUnits = settings.DailyGoalStudyUnits,
            SelectedDaysOfWeek = settings.SelectedDaysOfWeek.ToList()
        };
    }

    private static bool PlanMatches(RoutinePlanPeriod period, RoutineSettings settings)
    {
        return period.GoalMode == settings.GoalMode &&
               GetActiveGoalValue(period) == GetActiveGoalValue(settings) &&
               period.SelectedDaysOfWeek.SequenceEqual(settings.SelectedDaysOfWeek);
    }

    private static List<RoutinePlanPeriod> ClonePlanPeriods(IEnumerable<RoutinePlanPeriod>? periods)
    {
        return (periods ?? [])
            .Select(period => new RoutinePlanPeriod
            {
                StartDate = period.StartDate,
                EndDate = period.EndDate,
                GoalMode = NormalizeGoalMode(period.GoalMode),
                DailyGoalMinutes = period.DailyGoalMinutes,
                DailyGoalStudyUnits = period.DailyGoalStudyUnits,
                SelectedDaysOfWeek = period.SelectedDaysOfWeek?.ToList() ?? []
            })
            .ToList();
    }

    private static List<RoutineSuspensionPeriod> CloneSuspensionPeriods(IEnumerable<RoutineSuspensionPeriod>? periods)
    {
        return (periods ?? [])
            .Select(period => new RoutineSuspensionPeriod
            {
                StartDate = period.StartDate,
                EndDate = period.EndDate,
                Reason = period.Reason
            })
            .ToList();
    }

    private static void NormalizeSettings(RoutineSettings settings, bool migrateLegacyPlanPeriods = false)
    {
        settings.GoalMode = NormalizeGoalMode(settings.GoalMode);
        settings.DailyGoalMinutes = Math.Max(0, settings.DailyGoalMinutes);
        settings.DailyGoalStudyUnits = Math.Max(1, settings.DailyGoalStudyUnits);
        settings.SelectedDaysOfWeek = NormalizeSelectedDays(settings.SelectedDaysOfWeek);
        settings.PlanPeriods ??= [];
        settings.PlanPeriods = settings.PlanPeriods
            .Where(period => period.StartDate != default)
            .Select(period => new RoutinePlanPeriod
            {
                StartDate = period.StartDate.Date,
                EndDate = period.EndDate?.Date,
                GoalMode = NormalizeGoalMode(period.GoalMode),
                DailyGoalMinutes = Math.Max(0, period.DailyGoalMinutes),
                DailyGoalStudyUnits = Math.Max(1, period.DailyGoalStudyUnits),
                SelectedDaysOfWeek = NormalizeSelectedDays(period.SelectedDaysOfWeek)
            })
            .Where(HasValidPlanPeriod)
            .Where(period => period.SelectedDaysOfWeek.Count > 0)
            .Where(period => !period.EndDate.HasValue || period.EndDate.Value.Date >= period.StartDate.Date)
            .OrderBy(period => period.StartDate)
            .ToList();

        if (migrateLegacyPlanPeriods &&
            settings.PlanPeriods.Count == 0 &&
            HasValidCurrentPlan(settings) &&
            settings.LastUpdatedAt != DateTime.MinValue)
        {
            settings.PlanPeriods.Add(CreatePlanPeriod(settings, settings.LastUpdatedAt.Date));
        }

        settings.SuspensionPeriods ??= [];
        settings.SuspensionPeriods = settings.SuspensionPeriods
            .Where(period => period.StartDate != default)
            .Select(period => new RoutineSuspensionPeriod
            {
                StartDate = period.StartDate.Date,
                EndDate = period.EndDate?.Date,
                Reason = period.Reason
            })
            .Where(period => !period.EndDate.HasValue || period.EndDate.Value.Date >= period.StartDate.Date)
            .OrderBy(period => period.StartDate)
            .ToList();
    }

    private static List<DayOfWeek> NormalizeSelectedDays(IEnumerable<DayOfWeek>? days)
    {
        return (days ?? [])
            .Where(day => Enum.IsDefined(typeof(DayOfWeek), day))
            .Distinct()
            .OrderBy(day => (int)day)
            .ToList();
    }

    private static DailyStudyRecord GetOrCreateRecord(List<DailyStudyRecord> allRecords, Guid courseId, DateTime date)
    {
        var recordItem = allRecords.FirstOrDefault(r => r.Date.Date == date.Date);
        if (recordItem != null)
        {
            NormalizeRecord(courseId, recordItem);
            return recordItem;
        }

        recordItem = new DailyStudyRecord
        {
            CourseId = courseId,
            Date = date.Date
        };
        NormalizeRecord(courseId, recordItem);
        allRecords.Add(recordItem);
        return recordItem;
    }

    private static void NormalizeRecord(Guid courseId, DailyStudyRecord record)
    {
        record.CourseId = courseId;
        record.NonLessonMinutesStudied = Math.Max(0, record.NonLessonMinutesStudied);
        record.LessonCredits ??= [];
        record.CompletedStudyUnitIds = record.CompletedStudyUnitIds;

        if (record.NonLessonMinutesStudied == 0 &&
            record.LessonCredits.Count == 0 &&
            record.MinutesStudied > 0)
        {
            record.NonLessonMinutesStudied = record.MinutesStudied;
        }

        record.LessonCredits = record.LessonCredits
            .Where(credit => credit.LessonId != Guid.Empty && credit.MinutesCredited > 0)
            .GroupBy(credit => credit.LessonId)
            .Select(group => new LessonStudyCredit
            {
                LessonId = group.Key,
                MinutesCredited = group.Sum(item => item.MinutesCredited)
            })
            .OrderBy(credit => credit.LessonId)
            .ToList();

        record.MinutesStudied = record.NonLessonMinutesStudied + record.LessonCredits.Sum(credit => credit.MinutesCredited);
    }

    private static void ApplyStatus(DailyStudyRecord recordItem, RoutineSettings settings, DateTime? effectiveStartDate)
    {
        var dailyGoal = ResolveDailyGoal(recordItem, settings, effectiveStartDate);
        recordItem.DailyGoalMinutesAtTheTime = dailyGoal.HasValue && dailyGoal.Value.GoalMode == DailyGoalMode.TimeMinutes
            ? dailyGoal.Value.GoalValue
            : 0;

        if (!dailyGoal.HasValue)
        {
            recordItem.Status = DailyStudyStatus.Unplanned;
            return;
        }

        var completedGoalValue = GetCompletedGoalValue(recordItem, dailyGoal.Value.GoalMode);
        var compliance = CalculateCompliancePercentage(completedGoalValue, dailyGoal.Value.GoalValue);
        if (compliance == 0)
        {
            recordItem.Status = DailyStudyStatus.NotStarted;
        }
        else if (compliance < 50)
        {
            recordItem.Status = DailyStudyStatus.Partial;
        }
        else if (compliance < 100)
        {
            recordItem.Status = DailyStudyStatus.AlmostCompleted;
        }
        else
        {
            recordItem.Status = DailyStudyStatus.Completed;
        }
    }

    private static List<DailyGoalEvaluation> BuildMonthlyGoalEvaluations(
        Guid courseId,
        IReadOnlyCollection<DailyStudyRecord> monthlyRecords,
        RoutineSettings settings,
        DateTime? effectiveStartDate,
        int year,
        int month,
        DateTime today)
    {
        var isCurrentMonth = year == today.Year && month == today.Month;
        var evaluations = monthlyRecords
            .OrderBy(record => record.Date)
            .Select(record => BuildDailyGoalEvaluation(courseId, record, settings, effectiveStartDate, isCurrentMonth, today))
            .ToList();

        ApplyMonthlyCreditDistribution(evaluations, isCurrentMonth ? today.Date : null);
        return evaluations;
    }

    private static DailyGoalEvaluation BuildDailyGoalEvaluation(
        Guid courseId,
        DailyStudyRecord record,
        RoutineSettings settings,
        DateTime? effectiveStartDate,
        bool isCurrentMonth,
        DateTime today)
    {
        var isFutureDay = isCurrentMonth && record.Date.Date > today;
        var isPlannedDay = record.Status != DailyStudyStatus.Unplanned;
        var dailyGoal = isPlannedDay ? ResolveDailyGoal(record, settings, effectiveStartDate) : null;
        var goalMode = dailyGoal?.GoalMode ?? DailyGoalMode.TimeMinutes;
        var goalValue = dailyGoal?.GoalValue ?? 0;
        var completedGoalValue = dailyGoal.HasValue ? GetCompletedGoalValue(record, goalMode) : 0;
        var rawCompliance = isPlannedDay ? CalculateCompliancePercentage(completedGoalValue, goalValue) : 0;
        var isTimeGoal = goalMode == DailyGoalMode.TimeMinutes;
        var extraMinutes = isTimeGoal && isPlannedDay && !isFutureDay && goalValue > 0
            ? Math.Max(0, record.MinutesStudied - goalValue)
            : 0;
        var missingMinutes = isTimeGoal && isPlannedDay && !isFutureDay && goalValue > 0
            ? Math.Max(0, goalValue - record.MinutesStudied)
            : 0;
        var countsAsEffectiveGoalMet = isPlannedDay && !isFutureDay && goalValue > 0 && completedGoalValue >= goalValue;

        return new DailyGoalEvaluation
        {
            CourseId = courseId,
            Date = record.Date.Date,
            GoalMode = goalMode,
            GoalValueAtTheTime = goalValue,
            CompletedGoalValue = completedGoalValue,
            GoalUnit = GetGoalUnit(goalMode),
            RawStatus = record.Status,
            MinutesStudied = record.MinutesStudied,
            DailyGoalMinutesAtTheTime = isTimeGoal ? goalValue : 0,
            ExtraMinutes = extraMinutes,
            MissingMinutes = missingMinutes,
            ConsumedMonthlyCreditMinutes = 0,
            AvailableMonthlyCreditMinutes = 0,
            IsMonthlyCreditApplied = false,
            RawCompliancePercentage = rawCompliance,
            EffectiveCompliancePercentage = countsAsEffectiveGoalMet ? 100d : rawCompliance,
            CountsAsEffectiveGoalMet = countsAsEffectiveGoalMet,
            IsPlannedDay = isPlannedDay,
            IsFutureDay = isFutureDay
        };
    }

    private static void ApplyMonthlyCreditDistribution(List<DailyGoalEvaluation> evaluations, DateTime? currentDate)
    {
        var remainingCredit = evaluations
            .Where(CanGenerateMonthlyCredit)
            .Sum(evaluation => evaluation.ExtraMinutes);

        foreach (var evaluation in evaluations
                     .Where(evaluation => CanReceiveMonthlyCredit(evaluation, currentDate))
                     .OrderByDescending(evaluation => evaluation.Date))
        {
            if (remainingCredit < evaluation.MissingMinutes)
            {
                break;
            }

            evaluation.IsMonthlyCreditApplied = true;
            evaluation.ConsumedMonthlyCreditMinutes = evaluation.MissingMinutes;
            evaluation.EffectiveCompliancePercentage = 100d;
            evaluation.CountsAsEffectiveGoalMet = true;
            remainingCredit -= evaluation.MissingMinutes;
        }

        foreach (var evaluation in evaluations)
        {
            evaluation.AvailableMonthlyCreditMinutes = remainingCredit;
        }
    }

    private static bool CanGenerateMonthlyCredit(DailyGoalEvaluation evaluation)
    {
        return evaluation.GoalMode == DailyGoalMode.TimeMinutes &&
               evaluation.IsPlannedDay &&
               !evaluation.IsFutureDay &&
               evaluation.DailyGoalMinutesAtTheTime > 0 &&
               evaluation.ExtraMinutes > 0;
    }

    private static bool CanReceiveMonthlyCredit(DailyGoalEvaluation evaluation, DateTime? currentDate)
    {
        return evaluation.GoalMode == DailyGoalMode.TimeMinutes &&
               evaluation.IsPlannedDay &&
               !evaluation.IsFutureDay &&
               (!currentDate.HasValue || evaluation.Date.Date < currentDate.Value.Date) &&
               evaluation.DailyGoalMinutesAtTheTime > 0 &&
               evaluation.MissingMinutes > 0 &&
               !evaluation.CountsAsEffectiveGoalMet;
    }

    private static DailyGoalSnapshot? ResolveDailyGoal(DailyStudyRecord recordItem, RoutineSettings settings, DateTime? effectiveStartDate)
    {
        // Days before the course effectively existed in StudyHub must stay outside the routine window.
        if (effectiveStartDate.HasValue && recordItem.Date.Date < effectiveStartDate.Value.Date)
        {
            return null;
        }

        var recordDate = recordItem.Date.Date;
        if (IsSuspendedDay(recordDate, settings))
        {
            return null;
        }

        var activePlan = settings.PlanPeriods
            .Where(period => recordDate >= period.StartDate.Date)
            .Where(period => !period.EndDate.HasValue || recordDate <= period.EndDate.Value.Date)
            .OrderByDescending(period => period.StartDate)
            .FirstOrDefault();

        if (activePlan != null)
        {
            if (!activePlan.SelectedDaysOfWeek.Contains(recordDate.DayOfWeek))
            {
                return null;
            }

            return activePlan.GoalMode switch
            {
                DailyGoalMode.StudyUnits => new DailyGoalSnapshot(DailyGoalMode.StudyUnits, activePlan.DailyGoalStudyUnits),
                _ => new DailyGoalSnapshot(DailyGoalMode.TimeMinutes, activePlan.DailyGoalMinutes)
            };
        }

        var firstKnownPlanDate = settings.PlanPeriods
            .Select(period => period.StartDate.Date)
            .OrderBy(date => date)
            .FirstOrDefault();

        var isBeforeKnownPlanHistory = firstKnownPlanDate == default || recordDate < firstKnownPlanDate;
        return isBeforeKnownPlanHistory && recordItem.DailyGoalMinutesAtTheTime > 0
            ? new DailyGoalSnapshot(DailyGoalMode.TimeMinutes, recordItem.DailyGoalMinutesAtTheTime)
            : null;
    }

    private static DailyStudyRecord ResolveRecordForStreak(
        Guid courseId,
        DateTime date,
        IReadOnlyDictionary<DateTime, DailyStudyRecord> recordsByDate,
        RoutineSettings settings,
        DateTime? effectiveStartDate)
    {
        if (recordsByDate.TryGetValue(date.Date, out var record))
        {
            return record;
        }

        record = new DailyStudyRecord
        {
            CourseId = courseId,
            Date = date.Date
        };
        NormalizeRecord(courseId, record);
        ApplyStatus(record, settings, effectiveStartDate);
        return record;
    }

    private static bool HasStudyActivity(DailyStudyRecord record)
    {
        return record.MinutesStudied > 0 || record.CompletedStudyUnitCount > 0;
    }

    private static int GetCompletedGoalValue(DailyStudyRecord record, DailyGoalMode goalMode)
    {
        return goalMode switch
        {
            DailyGoalMode.StudyUnits => record.CompletedStudyUnitCount,
            _ => record.MinutesStudied
        };
    }

    private static double CalculateCompliancePercentage(int completedGoalValue, int goalValue)
    {
        return goalValue > 0
            ? Math.Min(100.0, (double)completedGoalValue / goalValue * 100)
            : 0;
    }

    private static string GetGoalUnit(DailyGoalMode goalMode)
    {
        return goalMode switch
        {
            DailyGoalMode.StudyUnits => StudyUnitsGoalUnit,
            _ => MinutesGoalUnit
        };
    }

    private static bool HasValidPlanPeriod(RoutinePlanPeriod period)
    {
        return HasValidGoal(period.GoalMode, period.DailyGoalMinutes, period.DailyGoalStudyUnits);
    }

    private static bool HasValidGoal(DailyGoalMode goalMode, int dailyGoalMinutes, int dailyGoalStudyUnits)
    {
        return goalMode switch
        {
            DailyGoalMode.StudyUnits => dailyGoalStudyUnits > 0,
            _ => dailyGoalMinutes > 0
        };
    }

    private static int GetActiveGoalValue(RoutineSettings settings)
    {
        return settings.GoalMode switch
        {
            DailyGoalMode.StudyUnits => settings.DailyGoalStudyUnits,
            _ => settings.DailyGoalMinutes
        };
    }

    private static int GetActiveGoalValue(RoutinePlanPeriod period)
    {
        return period.GoalMode switch
        {
            DailyGoalMode.StudyUnits => period.DailyGoalStudyUnits,
            _ => period.DailyGoalMinutes
        };
    }

    private static DailyGoalMode NormalizeGoalMode(DailyGoalMode goalMode)
    {
        return Enum.IsDefined(typeof(DailyGoalMode), goalMode)
            ? goalMode
            : DailyGoalMode.TimeMinutes;
    }

    private static bool IsSuspendedDay(DateTime date, RoutineSettings settings)
    {
        NormalizeSettings(settings);

        return settings.SuspensionPeriods.Any(period =>
            date.Date >= period.StartDate.Date &&
            (!period.EndDate.HasValue || date.Date <= period.EndDate.Value.Date));
    }

    private async Task<DateTime?> ResolveEffectiveCourseStartDateAsync(
        Guid courseId,
        IReadOnlyCollection<DailyStudyRecord> allRecords,
        RoutineSettings settings)
    {
        var addedAt = await GetCourseAddedAtAsync(courseId);
        return ResolveEffectiveCourseStartDate(addedAt, allRecords, settings);
    }

    private static DateTime? ResolveEffectiveCourseStartDate(
        DateTime? addedAt,
        IReadOnlyCollection<DailyStudyRecord> allRecords,
        RoutineSettings settings)
    {
        if (addedAt.HasValue)
        {
            return addedAt.Value.Date;
        }

        var firstRecordedDate = allRecords
            .Where(record => record.Date != default)
            .Select(record => record.Date.Date)
            .OrderBy(date => date)
            .FirstOrDefault();

        if (firstRecordedDate != default)
        {
            return firstRecordedDate;
        }

        var firstPlanStartDate = settings.PlanPeriods
            .Where(period => period.StartDate != default)
            .Select(period => period.StartDate.Date)
            .OrderBy(date => date)
            .FirstOrDefault();

        if (firstPlanStartDate != default)
        {
            return firstPlanStartDate;
        }

        if (settings.LastUpdatedAt != DateTime.MinValue)
        {
            return settings.LastUpdatedAt.Date;
        }

        return null;
    }

    private async Task<Dictionary<Guid, DateTime?>> GetCourseAddedAtMapAsync(
        IReadOnlyCollection<Guid> courseIds)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var rows = await context.Courses
            .AsNoTracking()
            .Where(course => courseIds.Contains(course.Id))
            .Select(course => new { course.Id, course.AddedAt })
            .ToListAsync();

        return rows.ToDictionary(
            row => row.Id,
            row => row.AddedAt != default ? (DateTime?)row.AddedAt : null);
    }

    private async Task<DateTime?> GetCourseAddedAtAsync(Guid courseId)
    {
        if (courseId == Guid.Empty)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var addedAt = await context.Courses
            .AsNoTracking()
            .Where(course => course.Id == courseId)
            .Select(course => (DateTime?)course.AddedAt)
            .FirstOrDefaultAsync();

        return addedAt.HasValue && addedAt.Value != default
            ? addedAt.Value
            : null;
    }
}
