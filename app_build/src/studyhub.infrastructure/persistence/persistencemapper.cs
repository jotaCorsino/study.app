using System.Text.Json;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;
using studyhub.shared.Enums;

namespace studyhub.infrastructure.persistence;

internal static class PersistenceMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Course ToDomain(this CourseRecord record)
    {
        var sourceMetadata = DeserializeCourseSourceMetadata(record);

        return new Course
        {
            Id = record.Id,
            RawTitle = string.IsNullOrWhiteSpace(record.RawTitle) ? record.Title : record.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(record.RawDescription) ? record.Description : record.RawDescription,
            Title = record.Title,
            Description = record.Description,
            Category = record.Category,
            ThumbnailUrl = record.ThumbnailUrl,
            SourceType = record.SourceType,
            SourceMetadata = sourceMetadata,
            TotalDuration = TimeSpan.FromMinutes(record.TotalDurationMinutes),
            AddedAt = record.AddedAt,
            LastAccessedAt = record.LastAccessedAt,
            Modules = record.Modules
                .OrderBy(module => module.Order)
                .Select(ToDomain)
                .ToList()
        };
    }

    public static CourseRecord ToRecord(this Course course)
    {
        var sourceMetadata = NormalizeCourseSourceMetadata(course);

        return new CourseRecord
        {
            Id = course.Id,
            RawTitle = string.IsNullOrWhiteSpace(course.RawTitle) ? course.Title : course.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(course.RawDescription) ? course.Description : course.RawDescription,
            Title = course.Title,
            Description = course.Description,
            Category = course.Category,
            ThumbnailUrl = course.ThumbnailUrl,
            FolderPath = sourceMetadata.RootPath,
            SourceType = course.SourceType,
            SourceMetadataJson = SerializeCourseSourceMetadata(sourceMetadata),
            TotalDurationMinutes = ConvertDuration(course.TotalDuration),
            AddedAt = course.AddedAt,
            LastAccessedAt = course.LastAccessedAt,
            CurrentLessonId = ResolveCurrentLessonId(course),
            Modules = course.Modules
                .OrderBy(module => module.Order)
                .Select(module => ToRecord(module, course.Id))
                .ToList()
        };
    }

    public static List<RoadmapLevel> DeserializeRoadmap(string json)
        => JsonSerializer.Deserialize<List<RoadmapLevel>>(json, JsonOptions) ?? [];

    public static string SerializeRoadmap(List<RoadmapLevel> roadmapLevels)
        => JsonSerializer.Serialize(roadmapLevels, JsonOptions);

    public static List<Material> DeserializeMaterials(string json)
        => JsonSerializer.Deserialize<List<Material>>(json, JsonOptions) ?? [];

    public static string SerializeMaterials(List<Material> materials)
        => JsonSerializer.Serialize(materials, JsonOptions);

    public static string SerializeCourseSourceMetadata(CourseSourceMetadata metadata)
        => JsonSerializer.Serialize(metadata ?? new CourseSourceMetadata(), JsonOptions);

    private static Module ToDomain(ModuleRecord record)
    {
        return new Module
        {
            Id = record.Id,
            CourseId = record.CourseId,
            Order = record.Order,
            RawTitle = string.IsNullOrWhiteSpace(record.RawTitle) ? record.Title : record.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(record.RawDescription) ? record.Description : record.RawDescription,
            Title = record.Title,
            Description = record.Description,
            Topics = record.Topics
                .OrderBy(topic => topic.Order)
                .Select(ToDomain)
                .ToList()
        };
    }

    private static Topic ToDomain(TopicRecord record)
    {
        return new Topic
        {
            Id = record.Id,
            ModuleId = record.ModuleId,
            Order = record.Order,
            RawTitle = string.IsNullOrWhiteSpace(record.RawTitle) ? record.Title : record.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(record.RawDescription) ? record.Description : record.RawDescription,
            Title = record.Title,
            Description = record.Description,
            Lessons = record.Lessons
                .OrderBy(lesson => lesson.Order)
                .Select(ToDomain)
                .ToList()
        };
    }

    private static Lesson ToDomain(LessonRecord record)
    {
        var localFilePath = string.IsNullOrWhiteSpace(record.LocalFilePath)
            ? record.FilePath
            : record.LocalFilePath;

        return new Lesson
        {
            Id = record.Id,
            TopicId = record.TopicId,
            Order = record.Order,
            RawTitle = string.IsNullOrWhiteSpace(record.RawTitle) ? record.Title : record.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(record.RawDescription) ? record.Description : record.RawDescription,
            Title = record.Title,
            Description = record.Description,
            SourceType = record.SourceType,
            LocalFilePath = localFilePath,
            ExternalUrl = record.ExternalUrl,
            Provider = record.Provider,
            Duration = TimeSpan.FromMinutes(record.DurationMinutes),
            Status = record.Status,
            WatchedPercentage = record.WatchedPercentage,
            LastPlaybackPosition = TimeSpan.FromSeconds(record.LastPlaybackPositionSeconds)
        };
    }

    private static ModuleRecord ToRecord(Module module, Guid courseId)
    {
        return new ModuleRecord
        {
            Id = module.Id,
            CourseId = courseId,
            Order = module.Order,
            RawTitle = string.IsNullOrWhiteSpace(module.RawTitle) ? module.Title : module.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(module.RawDescription) ? module.Description : module.RawDescription,
            Title = module.Title,
            Description = module.Description,
            Topics = module.Topics
                .OrderBy(topic => topic.Order)
                .Select(topic => ToRecord(topic, module.Id))
                .ToList()
        };
    }

    private static TopicRecord ToRecord(Topic topic, Guid moduleId)
    {
        return new TopicRecord
        {
            Id = topic.Id,
            ModuleId = moduleId,
            Order = topic.Order,
            RawTitle = string.IsNullOrWhiteSpace(topic.RawTitle) ? topic.Title : topic.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(topic.RawDescription) ? topic.Description : topic.RawDescription,
            Title = topic.Title,
            Description = topic.Description,
            Lessons = topic.Lessons
                .OrderBy(lesson => lesson.Order)
                .Select(lesson => ToRecord(lesson, topic.Id))
                .ToList()
        };
    }

    private static LessonRecord ToRecord(Lesson lesson, Guid topicId)
    {
        var localFilePath = string.IsNullOrWhiteSpace(lesson.LocalFilePath)
            ? lesson.FilePath
            : lesson.LocalFilePath;

        return new LessonRecord
        {
            Id = lesson.Id,
            TopicId = topicId,
            Order = lesson.Order,
            RawTitle = string.IsNullOrWhiteSpace(lesson.RawTitle) ? lesson.Title : lesson.RawTitle,
            RawDescription = string.IsNullOrWhiteSpace(lesson.RawDescription) ? lesson.Description : lesson.RawDescription,
            Title = lesson.Title,
            Description = lesson.Description,
            FilePath = localFilePath,
            SourceType = lesson.SourceType,
            LocalFilePath = localFilePath,
            ExternalUrl = lesson.ExternalUrl,
            Provider = lesson.Provider,
            DurationMinutes = ConvertDuration(lesson.Duration),
            Status = lesson.Status,
            WatchedPercentage = lesson.WatchedPercentage,
            LastPlaybackPositionSeconds = ConvertPlaybackPosition(lesson.LastPlaybackPosition)
        };
    }

    private static Guid? ResolveCurrentLessonId(Course course)
    {
        var orderedLessons = course.Modules
            .OrderBy(module => module.Order)
            .SelectMany(module => module.Topics.OrderBy(topic => topic.Order))
            .SelectMany(topic => topic.Lessons.OrderBy(lesson => lesson.Order))
            .ToList();

        return orderedLessons
            .FirstOrDefault(lesson => lesson.Status == LessonStatus.InProgress)?.Id
            ?? orderedLessons.LastOrDefault(lesson => lesson.Status == LessonStatus.Completed)?.Id;
    }

    private static int ConvertDuration(TimeSpan duration)
        => Math.Max(0, (int)Math.Round(duration.TotalMinutes));

    private static int ConvertPlaybackPosition(TimeSpan position)
        => Math.Max(0, (int)Math.Round(position.TotalSeconds));

    private static CourseSourceMetadata DeserializeCourseSourceMetadata(CourseRecord record)
    {
        CourseSourceMetadata? metadata = null;

        if (!string.IsNullOrWhiteSpace(record.SourceMetadataJson))
        {
            metadata = JsonSerializer.Deserialize<CourseSourceMetadata>(record.SourceMetadataJson, JsonOptions);
        }

        metadata ??= new CourseSourceMetadata();

        if (record.SourceType == CourseSourceType.LocalFolder &&
            string.IsNullOrWhiteSpace(metadata.RootPath))
        {
            metadata.RootPath = record.FolderPath;
        }

        return metadata;
    }

    private static CourseSourceMetadata NormalizeCourseSourceMetadata(Course course)
    {
        var metadata = course.SourceMetadata ?? new CourseSourceMetadata();

        if (course.SourceType == CourseSourceType.LocalFolder &&
            string.IsNullOrWhiteSpace(metadata.RootPath))
        {
            metadata.RootPath = course.FolderPath;
        }

        return metadata;
    }
}
