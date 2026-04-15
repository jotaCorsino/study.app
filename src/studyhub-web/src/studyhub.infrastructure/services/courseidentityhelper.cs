using System.Security.Cryptography;
using System.Text;

namespace studyhub.infrastructure.services;

internal static class CourseIdentityHelper
{
    public static Guid CreateModuleId(Guid courseId, int moduleOrder)
        => CreateDeterministicGuid($"{courseId:N}:module:{moduleOrder}");

    public static Guid CreateTopicId(Guid courseId, int moduleOrder)
        => CreateDeterministicGuid($"{courseId:N}:module:{moduleOrder}:topic:1");

    public static Guid CreateLessonId(Guid courseId, int moduleOrder, int lessonOrder, string sourceKey)
        => CreateDeterministicGuid($"{courseId:N}:module:{moduleOrder}:lesson:{lessonOrder}:{sourceKey}");

    public static Guid CreateExternalCourseId(string provider, string externalCourseId)
        => CreateDeterministicGuid($"external:{NormalizeKey(provider)}:course:{NormalizeKey(externalCourseId)}");

    public static Guid CreateExternalDisciplineModuleId(Guid courseId, string disciplineKey)
        => CreateDeterministicGuid($"{courseId:N}:external-discipline:{NormalizeKey(disciplineKey)}");

    public static Guid CreateExternalTopicId(Guid moduleId, string moduleKey)
        => CreateDeterministicGuid($"{moduleId:N}:external-module:{NormalizeKey(moduleKey)}");

    public static Guid CreateExternalLessonId(Guid courseId, string disciplineKey, string moduleKey, string lessonKey)
        => CreateDeterministicGuid($"{courseId:N}:external-lesson:{NormalizeKey(disciplineKey)}:{NormalizeKey(moduleKey)}:{NormalizeKey(lessonKey)}");

    public static Guid CreateExternalAssessmentId(Guid courseId, string disciplineKey, string assessmentKey)
        => CreateDeterministicGuid($"{courseId:N}:external-assessment:{NormalizeKey(disciplineKey)}:{NormalizeKey(assessmentKey)}");

    private static Guid CreateDeterministicGuid(string seed)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }

    private static string NormalizeKey(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();
}
