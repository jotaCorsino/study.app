using System.Text.Json;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;

namespace studyhub.infrastructure.services;

public class RoutineService : IRoutineService
{
    private readonly string _baseDirectory;

    public RoutineService(IStoragePathsService storagePathsService)
    {
        _baseDirectory = storagePathsService.RoutineDirectory;
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
        var record = allRecords.FirstOrDefault(r => r.Date.Date == date.Date) ?? new DailyStudyRecord
        {
            CourseId = courseId,
            Date = date.Date
        };

        NormalizeRecord(courseId, record);
        ApplyStatus(record, settings);
        return record;
    }

    public async Task<List<DailyStudyRecord>> GetMonthlyRecordsAsync(Guid courseId, int year, int month)
    {
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);
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
            ApplyStatus(record, settings);
            records.Add(record);
        }

        return records;
    }

    public async Task AddStudyTimeAsync(Guid courseId, int minutes)
    {
        if (minutes <= 0) return;

        var date = DateTime.Now.Date;
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);

        var recordItem = GetOrCreateRecord(allRecords, courseId, date);
        recordItem.NonLessonMinutesStudied += minutes;
        NormalizeRecord(courseId, recordItem);
        ApplyStatus(recordItem, settings);

        await SaveAllRecordsAsync(courseId, allRecords);
    }

    public async Task CreditLessonProgressAsync(Guid courseId, Guid lessonId, int totalLessonMinutes, int desiredTotalMinutes, DateTime? date = null)
    {
        if (courseId == Guid.Empty || lessonId == Guid.Empty)
        {
            return;
        }

        var normalizedDuration = Math.Max(0, totalLessonMinutes);
        if (normalizedDuration <= 0)
        {
            return;
        }

        var targetMinutes = Math.Clamp(desiredTotalMinutes, 0, normalizedDuration);
        if (targetMinutes <= 0)
        {
            return;
        }

        var studyDate = (date ?? DateTime.Now).Date;
        var allRecords = await GetAllRecordsAsync(courseId);
        var settings = await GetSettingsAsync(courseId);

        var creditedAcrossCourse = allRecords
            .SelectMany(record => record.LessonCredits)
            .Where(credit => credit.LessonId == lessonId)
            .Sum(credit => Math.Max(0, credit.MinutesCredited));

        if (creditedAcrossCourse >= targetMinutes)
        {
            return;
        }

        var remainingMinutes = targetMinutes - creditedAcrossCourse;
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

        lessonCredit.MinutesCredited += remainingMinutes;
        NormalizeRecord(courseId, recordItem);
        ApplyStatus(recordItem, settings);

        await SaveAllRecordsAsync(courseId, allRecords);
    }

    public async Task<int> GetCurrentStreakAsync(Guid courseId, DateTime? referenceDate = null)
    {
        var settings = await GetSettingsAsync(courseId);
        var studiedDates = (await GetAllRecordsAsync(courseId))
            .Select(record =>
            {
                NormalizeRecord(courseId, record);
                ApplyStatus(record, settings);
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
        foreach (var record in allRecords)
        {
            NormalizeRecord(courseId, record);
            ApplyStatus(record, settings);
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

    private static void ApplyStatus(DailyStudyRecord recordItem, RoutineSettings settings)
    {
        var isPlannedDay = IsPlannedDay(recordItem, settings);
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

    private static bool IsPlannedDay(DailyStudyRecord recordItem, RoutineSettings settings)
    {
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
}
