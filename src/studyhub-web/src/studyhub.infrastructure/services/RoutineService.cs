using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;

namespace studyhub.infrastructure.services;

public class RoutineService : IRoutineService
{
    private readonly string _baseDirectory;
    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory;

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
            return JsonSerializer.Deserialize<RoutineSettings>(json) ?? new RoutineSettings();
        }
        catch
        {
            return new RoutineSettings();
        }
    }

    public async Task SaveSettingsAsync(Guid courseId, RoutineSettings settings)
    {
        settings.LastUpdatedAt = DateTime.Now;
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetSettingsPath(courseId), json);
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
        return BuildMonthlyGoalEvaluations(courseId, records, year, month, today.Date);
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

    public async Task<int> GetCurrentStreakAsync(Guid courseId, DateTime? referenceDate = null)
    {
        var settings = await GetSettingsAsync(courseId);
        var allRecords = await GetAllRecordsAsync(courseId);
        var effectiveStartDate = await ResolveEffectiveCourseStartDateAsync(courseId, allRecords, settings);
        var studiedDates = allRecords
            .Select(record =>
            {
                NormalizeRecord(courseId, record);
                ApplyStatus(record, settings, effectiveStartDate);
                return record;
            })
            .Where(record => record.Status != DailyStudyStatus.Unplanned && record.MinutesStudied > 0)
            .Select(record => record.Date.Date)
            .Distinct()
            .ToHashSet();

        if (studiedDates.Count == 0)
        {
            return 0;
        }

        var today = (referenceDate ?? DateTime.Now).Date;
        var anchor = studiedDates.Contains(today)
            ? today
            : studiedDates.Contains(today.AddDays(-1))
                ? today.AddDays(-1)
                : DateTime.MinValue;

        if (anchor == DateTime.MinValue)
        {
            return 0;
        }

        var streak = 0;
        var cursor = anchor;
        while (studiedDates.Contains(cursor))
        {
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
        var isPlannedDay = IsPlannedDay(recordItem, settings, effectiveStartDate);
        var preservedHistoricalGoal = recordItem.Date.Date < settings.LastUpdatedAt.Date && recordItem.DailyGoalMinutesAtTheTime > 0
            ? recordItem.DailyGoalMinutesAtTheTime
            : settings.DailyGoalMinutes;

        recordItem.DailyGoalMinutesAtTheTime = isPlannedDay ? Math.Max(0, preservedHistoricalGoal) : 0;

        if (!isPlannedDay)
        {
            recordItem.Status = DailyStudyStatus.Unplanned;
            return;
        }

        var compliance = recordItem.CompliancePercentage;
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
        int year,
        int month,
        DateTime today)
    {
        var isCurrentMonth = year == today.Year && month == today.Month;
        var evaluations = monthlyRecords
            .OrderBy(record => record.Date)
            .Select(record => BuildDailyGoalEvaluation(courseId, record, isCurrentMonth, today))
            .ToList();

        ApplyMonthlyCreditDistribution(evaluations, isCurrentMonth ? today.Date : null);
        return evaluations;
    }

    private static DailyGoalEvaluation BuildDailyGoalEvaluation(
        Guid courseId,
        DailyStudyRecord record,
        bool isCurrentMonth,
        DateTime today)
    {
        var isFutureDay = isCurrentMonth && record.Date.Date > today;
        var isPlannedDay = record.Status != DailyStudyStatus.Unplanned;
        var dailyGoal = isPlannedDay ? Math.Max(0, record.DailyGoalMinutesAtTheTime) : 0;
        var rawCompliance = isPlannedDay ? record.CompliancePercentage : 0;
        var extraMinutes = isPlannedDay && !isFutureDay && dailyGoal > 0
            ? Math.Max(0, record.MinutesStudied - dailyGoal)
            : 0;
        var missingMinutes = isPlannedDay && !isFutureDay && dailyGoal > 0
            ? Math.Max(0, dailyGoal - record.MinutesStudied)
            : 0;
        var countsAsEffectiveGoalMet = isPlannedDay && !isFutureDay && dailyGoal > 0 && record.MinutesStudied >= dailyGoal;

        return new DailyGoalEvaluation
        {
            CourseId = courseId,
            Date = record.Date.Date,
            RawStatus = record.Status,
            MinutesStudied = record.MinutesStudied,
            DailyGoalMinutesAtTheTime = dailyGoal,
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
        return evaluation.IsPlannedDay &&
               !evaluation.IsFutureDay &&
               evaluation.DailyGoalMinutesAtTheTime > 0 &&
               evaluation.ExtraMinutes > 0;
    }

    private static bool CanReceiveMonthlyCredit(DailyGoalEvaluation evaluation, DateTime? currentDate)
    {
        return evaluation.IsPlannedDay &&
               !evaluation.IsFutureDay &&
               (!currentDate.HasValue || evaluation.Date.Date < currentDate.Value.Date) &&
               evaluation.DailyGoalMinutesAtTheTime > 0 &&
               evaluation.MissingMinutes > 0 &&
               !evaluation.CountsAsEffectiveGoalMet;
    }

    private static bool IsPlannedDay(DailyStudyRecord recordItem, RoutineSettings settings, DateTime? effectiveStartDate)
    {
        // Days before the course effectively existed in StudyHub must stay outside the routine window.
        if (effectiveStartDate.HasValue && recordItem.Date.Date < effectiveStartDate.Value.Date)
        {
            return false;
        }

        if (settings.DailyGoalMinutes <= 0 || settings.SelectedDaysOfWeek.Count == 0)
        {
            return false;
        }

        if (settings.LastUpdatedAt != DateTime.MinValue && recordItem.Date.Date < settings.LastUpdatedAt.Date)
        {
            if (recordItem.Status == DailyStudyStatus.Unplanned)
            {
                return false;
            }

            if (recordItem.DailyGoalMinutesAtTheTime > 0)
            {
                return true;
            }
        }

        return settings.SelectedDaysOfWeek.Contains(recordItem.Date.DayOfWeek);
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
