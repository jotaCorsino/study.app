using studyhub.application.Contracts.LocalImport;
using studyhub.infrastructure.persistence.models;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class LocalCourseManifestValidatorTests
{
    [Fact]
    public void HasUsableStructure_AllowsEmptyKnownParentsWhenTheTreeContainsLessons()
    {
        var ids = CreateIds();
        var emptyModuleId = Guid.NewGuid();
        var emptyTopicId = Guid.NewGuid();
        var course = CreateCourse(ids);
        course.Modules[0].Topics.Add(new TopicRecord
        {
            Id = emptyTopicId,
            ModuleId = ids.ModuleId,
            Lessons = []
        });
        course.Modules.Add(new ModuleRecord
        {
            Id = emptyModuleId,
            CourseId = ids.CourseId,
            Topics = []
        });
        var manifest = CreateManifest(ids);
        manifest.Modules[0].Topics.Add(new DetectedTopicStructure
        {
            TopicId = emptyTopicId,
            RelativePath = "Topico vazio",
            Lessons = []
        });
        manifest.Modules.Add(new DetectedModuleStructure
        {
            ModuleId = emptyModuleId,
            RelativePath = "Modulo vazio",
            Topics = []
        });

        Assert.True(LocalCourseManifestValidator.HasUsableStructure(manifest));
        Assert.True(LocalCourseManifestValidator.HasMatchingPersistedIdentities(manifest, course));
    }

    [Fact]
    public void HasUsableStructure_RejectsTreeWithoutAnyLesson()
    {
        var manifest = new DetectedCourseStructure
        {
            CourseId = Guid.NewGuid(),
            Modules =
            [
                new DetectedModuleStructure
                {
                    ModuleId = Guid.NewGuid(),
                    RelativePath = "Modulo vazio",
                    Topics =
                    [
                        new DetectedTopicStructure
                        {
                            TopicId = Guid.NewGuid(),
                            RelativePath = "Topico vazio",
                            Lessons = []
                        }
                    ]
                }
            ]
        };

        Assert.False(LocalCourseManifestValidator.HasUsableStructure(manifest));
    }

    [Fact]
    public void TryCorrelatePersistedIdentities_ExactTreeMatchesEveryParentLevel()
    {
        var ids = CreateIds();
        var course = CreateCourse(ids);
        var manifest = CreateManifest(ids);

        var correlated = LocalCourseManifestValidator.TryCorrelatePersistedIdentities(
            manifest,
            course,
            out var correlation);

        Assert.True(correlated);
        Assert.True(correlation.IsExactMatch);
        Assert.True(correlation.MatchesModule(ids.ModuleId));
        Assert.True(correlation.MatchesTopic(ids.ModuleId, ids.TopicId));
        Assert.True(correlation.MatchesLesson(ids.TopicId, ids.LessonId));
        Assert.True(LocalCourseManifestValidator.HasMatchingPersistedIdentities(manifest, course));
    }

    [Fact]
    public void TryCorrelatePersistedIdentities_RejectsParentMismatchEvenWhenIdsMatch()
    {
        var ids = CreateIds();
        var otherModuleId = Guid.NewGuid();
        var otherTopicId = Guid.NewGuid();
        var otherLessonId = Guid.NewGuid();
        var course = CreateCourse(ids);
        course.Modules.Add(new ModuleRecord
        {
            Id = otherModuleId,
            CourseId = ids.CourseId,
            Topics =
            [
                new TopicRecord
                {
                    Id = otherTopicId,
                    ModuleId = otherModuleId,
                    Lessons =
                    [
                        new LessonRecord
                        {
                            Id = otherLessonId,
                            TopicId = otherTopicId
                        }
                    ]
                }
            ]
        });
        var manifest = CreateManifest(ids);
        manifest.Modules.Add(new DetectedModuleStructure
        {
            ModuleId = otherModuleId,
            RelativePath = "Modulo 02",
            Topics =
            [
                new DetectedTopicStructure
                {
                    TopicId = otherTopicId,
                    RelativePath = ".",
                    Lessons =
                    [
                        new DetectedLessonFile
                        {
                            LessonId = otherLessonId,
                            RelativePath = "Modulo 02/Aula.mp4"
                        }
                    ]
                }
            ]
        });
        var firstTopic = manifest.Modules[0].Topics.Single();
        var secondTopic = manifest.Modules[1].Topics.Single();
        manifest.Modules[0].Topics = [secondTopic];
        manifest.Modules[1].Topics = [firstTopic];

        var correlated = LocalCourseManifestValidator.TryCorrelatePersistedIdentities(
            manifest,
            course,
            out var correlation);

        Assert.True(correlated);
        Assert.False(correlation.IsExactMatch);
        Assert.False(correlation.MatchesTopic(ids.ModuleId, ids.TopicId));
        Assert.False(LocalCourseManifestValidator.HasMatchingPersistedIdentities(manifest, course));
    }

    [Fact]
    public void TryCorrelatePersistedIdentities_PartialManifestIsCorrelatedButNotExact()
    {
        var ids = CreateIds();
        var course = CreateCourse(ids);
        course.Modules.Add(new ModuleRecord
        {
            Id = Guid.NewGuid(),
            CourseId = ids.CourseId,
            Topics = []
        });
        var manifest = CreateManifest(ids);

        var correlated = LocalCourseManifestValidator.TryCorrelatePersistedIdentities(
            manifest,
            course,
            out var correlation);

        Assert.True(correlated);
        Assert.False(correlation.IsExactMatch);
        Assert.True(correlation.MatchesModule(ids.ModuleId));
        Assert.True(correlation.MatchesTopic(ids.ModuleId, ids.TopicId));
        Assert.True(correlation.MatchesLesson(ids.TopicId, ids.LessonId));
        Assert.False(LocalCourseManifestValidator.HasMatchingPersistedIdentities(manifest, course));
    }

    [Fact]
    public void TryCorrelatePersistedIdentities_DifferentCourseIdIsNotCorrelated()
    {
        var ids = CreateIds();
        var course = CreateCourse(ids);
        var manifest = CreateManifest(ids);
        manifest.CourseId = Guid.NewGuid();

        Assert.False(LocalCourseManifestValidator.TryCorrelatePersistedIdentities(
            manifest,
            course,
            out var correlation));
        Assert.False(correlation.IsExactMatch);
    }

    private static TestIds CreateIds()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static CourseRecord CreateCourse(TestIds ids)
        => new()
        {
            Id = ids.CourseId,
            Modules =
            [
                new ModuleRecord
                {
                    Id = ids.ModuleId,
                    CourseId = ids.CourseId,
                    Topics =
                    [
                        new TopicRecord
                        {
                            Id = ids.TopicId,
                            ModuleId = ids.ModuleId,
                            Lessons =
                            [
                                new LessonRecord
                                {
                                    Id = ids.LessonId,
                                    TopicId = ids.TopicId
                                }
                            ]
                        }
                    ]
                }
            ]
        };

    private static DetectedCourseStructure CreateManifest(TestIds ids)
        => new()
        {
            CourseId = ids.CourseId,
            Modules =
            [
                new DetectedModuleStructure
                {
                    ModuleId = ids.ModuleId,
                    RelativePath = "Modulo 01",
                    Topics =
                    [
                        new DetectedTopicStructure
                        {
                            TopicId = ids.TopicId,
                            RelativePath = "Topico 01",
                            Lessons =
                            [
                                new DetectedLessonFile
                                {
                                    LessonId = ids.LessonId,
                                    RelativePath = "Modulo 01/Topico 01/Aula.mp4"
                                }
                            ]
                        }
                    ]
                }
            ]
        };

    private sealed record TestIds(Guid CourseId, Guid ModuleId, Guid TopicId, Guid LessonId);
}
