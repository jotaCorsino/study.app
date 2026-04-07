using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface IRoutineService
{
    Task<RoutineSettings> GetSettingsAsync(Guid courseId);
    Task SaveSettingsAsync(Guid courseId, RoutineSettings settings);
    
    Task<DailyStudyRecord> GetDailyRecordAsync(Guid courseId, DateTime date);
    Task<List<DailyStudyRecord>> GetMonthlyRecordsAsync(Guid courseId, int year, int month);
    Task AddStudyTimeAsync(Guid courseId, int minutes);
    Task CreditLessonProgressAsync(Guid courseId, Guid lessonId, int creditedMinutes, DateTime? date = null);
    Task<int> GetCurrentStreakAsync(Guid courseId, DateTime? referenceDate = null);
}
