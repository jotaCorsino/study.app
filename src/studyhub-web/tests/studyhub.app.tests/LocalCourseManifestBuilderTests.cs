using System.Text.Json;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class LocalCourseManifestBuilderTests
{
    [Fact]
    public void Build_UsesPersistedIdentitiesAndPortablePathsForTheCompleteKnownTree()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "studyhub-manifest-builder-tests",
            Guid.NewGuid().ToString("N"));
        var staleRootPath = Path.Combine(
            Path.GetTempPath(),
            "studyhub-manifest-builder-stale",
            Guid.NewGuid().ToString("N"));
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var firstLessonId = Guid.NewGuid();
        var secondLessonId = Guid.NewGuid();
        var scannedAtUtc = new DateTime(2026, 7, 14, 17, 30, 0, DateTimeKind.Utc);
        var course = new CourseRecord
        {
            Id = courseId,
            RawTitle = "Curso persistido",
            Title = "Curso editado",
            FolderPath = staleRootPath,
            SourceType = CourseSourceType.LocalFolder,
            Modules =
            [
                new ModuleRecord
                {
                    Id = moduleId,
                    CourseId = courseId,
                    Order = 3,
                    RawTitle = "Modulo 03",
                    Title = "Módulo editado",
                    SourceRelativePath = "Modulo 03",
                    IsAvailable = false,
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = topicId,
                            ModuleId = moduleId,
                            Order = 2,
                            RawTitle = "Topico 02",
                            Title = "Tópico editado",
                            SourceRelativePath = "Modulo 03/Topico 02",
                            IsAvailable = false,
                            CompletedAtUtc = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = firstLessonId,
                                    TopicId = topicId,
                                    Order = 1,
                                    RawTitle = "Aula 01.mp4",
                                    Title = "Aula editada",
                                    RelativeFilePath = "Modulo 03/Topico 02/Aula 01.mp4",
                                    LocalFilePath = Path.Combine(staleRootPath, "stale.mp4"),
                                    FilePath = Path.Combine(staleRootPath, "stale.mp4"),
                                    IsAvailable = false,
                                    DurationMinutes = 12
                                },
                                new LessonRecord
                                {
                                    Id = secondLessonId,
                                    TopicId = topicId,
                                    Order = 2,
                                    RawTitle = "Aula 02.mp4",
                                    Title = "Aula 02",
                                    RelativeFilePath = string.Empty,
                                    LocalFilePath = Path.Combine(
                                        rootPath,
                                        "Modulo 03",
                                        "Topico 02",
                                        "Aula 02.mp4"),
                                    DurationMinutes = 8
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var manifest = LocalCourseManifestBuilder.Build(course, rootPath, scannedAtUtc);

        Assert.Equal(courseId, manifest.CourseId);
        Assert.Equal(Path.GetFullPath(rootPath), manifest.RootFolderPath);
        Assert.Equal(scannedAtUtc, manifest.ScannedAt);
        var manifestModule = Assert.Single(manifest.Modules);
        var manifestTopic = Assert.Single(manifestModule.Topics);
        var manifestLessons = manifestTopic.Lessons.OrderBy(lesson => lesson.Order).ToArray();
        Assert.Equal(moduleId, manifestModule.ModuleId);
        Assert.Equal(topicId, manifestTopic.TopicId);
        Assert.Equal("Modulo 03", manifestModule.RelativePath);
        Assert.Equal("Topico 02", manifestTopic.RelativePath);
        Assert.Equal([firstLessonId, secondLessonId], manifestLessons.Select(lesson => lesson.LessonId));
        Assert.Equal(
            [
                "Modulo 03/Topico 02/Aula 01.mp4",
                "Modulo 03/Topico 02/Aula 02.mp4"
            ],
            manifestLessons.Select(lesson => lesson.RelativePath));
        Assert.Equal(
            Path.Combine(rootPath, "Modulo 03", "Topico 02", "Aula 01.mp4"),
            manifestLessons[0].AbsolutePath);
        Assert.DoesNotContain(staleRootPath, manifestLessons[0].AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(LocalCourseManifestValidator.HasMatchingPersistedIdentities(manifest, course));

        var rootLessons = EnumerateRootLessons(manifest.RootNode).ToArray();
        Assert.Equal([firstLessonId, secondLessonId], rootLessons.Select(lesson => lesson.LessonId));
        Assert.All(rootLessons, lesson => Assert.StartsWith(
            Path.GetFullPath(rootPath),
            lesson.AbsolutePath,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_RejectsLessonPathsThatCannotBeRepresentedBelowTheRoot()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "studyhub-manifest-builder-tests",
            Guid.NewGuid().ToString("N"));
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var course = new CourseRecord
        {
            Id = courseId,
            FolderPath = rootPath,
            Modules =
            [
                new ModuleRecord
                {
                    Id = moduleId,
                    CourseId = courseId,
                    SourceRelativePath = ".",
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = topicId,
                            ModuleId = moduleId,
                            SourceRelativePath = ".",
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = lessonId,
                                    TopicId = topicId,
                                    RelativeFilePath = "../outside.mp4",
                                    LocalFilePath = Path.Combine(
                                        Path.GetTempPath(),
                                        "outside",
                                        "lesson.mp4")
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            LocalCourseManifestBuilder.Build(course, rootPath, DateTime.UtcNow));

        Assert.Contains(lessonId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_IncludesEmptyParentsAndUsesStableOrderPathAndIdTieBreakers()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "studyhub-manifest-builder-tests",
            Guid.NewGuid().ToString("N"));
        var courseId = Guid.NewGuid();
        var firstModuleId = StableGuid(1);
        var secondModuleId = StableGuid(2);
        var thirdModuleId = StableGuid(3);
        var firstTopicId = StableGuid(11);
        var secondTopicId = StableGuid(12);
        var thirdTopicId = StableGuid(13);
        var firstLessonId = StableGuid(21);
        var secondLessonId = StableGuid(22);
        var thirdLessonId = StableGuid(23);
        var scannedAtUtc = new DateTime(2026, 7, 14, 18, 0, 0, DateTimeKind.Utc);
        var course = new CourseRecord
        {
            Id = courseId,
            Modules =
            [
                new ModuleRecord
                {
                    Id = thirdModuleId,
                    CourseId = courseId,
                    Order = 1,
                    SourceRelativePath = "zeta",
                    Topics = []
                },
                new ModuleRecord
                {
                    Id = secondModuleId,
                    CourseId = courseId,
                    Order = 1,
                    SourceRelativePath = "alpha",
                    Topics = []
                },
                new ModuleRecord
                {
                    Id = firstModuleId,
                    CourseId = courseId,
                    Order = 1,
                    SourceRelativePath = "alpha",
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = thirdTopicId,
                            ModuleId = firstModuleId,
                            Order = 1,
                            SourceRelativePath = "alpha/zeta",
                            Lessons = []
                        },
                        new TopicRecord
                        {
                            Id = secondTopicId,
                            ModuleId = firstModuleId,
                            Order = 1,
                            SourceRelativePath = "alpha/topic",
                            Lessons = []
                        },
                        new TopicRecord
                        {
                            Id = firstTopicId,
                            ModuleId = firstModuleId,
                            Order = 1,
                            SourceRelativePath = "alpha/topic",
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = thirdLessonId,
                                    TopicId = firstTopicId,
                                    Order = 1,
                                    RelativeFilePath = "alpha/topic/zeta.mp4"
                                },
                                new LessonRecord
                                {
                                    Id = secondLessonId,
                                    TopicId = firstTopicId,
                                    Order = 1,
                                    RelativeFilePath = "alpha/topic/alpha.mp4"
                                },
                                new LessonRecord
                                {
                                    Id = firstLessonId,
                                    TopicId = firstTopicId,
                                    Order = 1,
                                    RelativeFilePath = "alpha/topic/alpha.mp4"
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var firstManifest = LocalCourseManifestBuilder.Build(course, rootPath, scannedAtUtc);
        var firstJson = JsonSerializer.Serialize(firstManifest);

        course.Modules.Reverse();
        foreach (var module in course.Modules)
        {
            module.Topics.Reverse();
            foreach (var topic in module.Topics)
            {
                topic.Lessons.Reverse();
            }
        }

        var secondManifest = LocalCourseManifestBuilder.Build(course, rootPath, scannedAtUtc);

        Assert.Equal(firstJson, JsonSerializer.Serialize(secondManifest));
        Assert.Equal(
            [firstModuleId, secondModuleId, thirdModuleId],
            firstManifest.Modules.Select(module => module.ModuleId));
        Assert.Equal(
            [firstTopicId, secondTopicId, thirdTopicId],
            firstManifest.Modules[0].Topics.Select(topic => topic.TopicId));
        Assert.Equal(
            [firstLessonId, secondLessonId, thirdLessonId],
            firstManifest.Modules[0].Topics[0].Lessons.Select(lesson => lesson.LessonId));
        Assert.Empty(firstManifest.Modules[1].Topics);
        Assert.Empty(firstManifest.Modules[0].Topics[1].Lessons);
        Assert.Empty(firstManifest.Modules[0].Topics[2].Lessons);
        Assert.True(LocalCourseManifestValidator.HasUsableStructure(firstManifest));
        Assert.True(LocalCourseManifestValidator.HasMatchingPersistedIdentities(firstManifest, course));
    }

    private static IEnumerable<studyhub.application.Contracts.LocalImport.DetectedLessonFile> EnumerateRootLessons(
        studyhub.application.Contracts.LocalImport.DetectedFolderNode node)
    {
        foreach (var lesson in node.DirectLessons)
        {
            yield return lesson;
        }

        foreach (var child in node.Children)
        {
            foreach (var lesson in EnumerateRootLessons(child))
            {
                yield return lesson;
            }
        }
    }

    private static Guid StableGuid(int value)
        => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}
