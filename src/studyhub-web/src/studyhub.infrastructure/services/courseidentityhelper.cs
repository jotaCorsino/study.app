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

    private static Guid CreateDeterministicGuid(string seed)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }
}
