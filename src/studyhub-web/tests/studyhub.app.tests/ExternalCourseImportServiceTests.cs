using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.services;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class ExternalCourseImportServiceTests
{
    [Fact]
    public void Parse_RejectsUnsupportedSchemaMajor()
    {
        var parser = new ExternalCourseJsonParser();

        var result = parser.Parse(
            """
            {
              "schemaVersion": "2.0.0",
              "source": { "provider": "univirtus" },
              "course": { "externalId": "provider:course:1", "title": "Curso" },
              "disciplines": []
            }
            """);

        Assert.False(result.Success);
        Assert.Equal(
            studyhub.application.Contracts.ExternalImport.ExternalCourseImportParseErrorKind.UnsupportedSchemaVersion,
            result.ErrorKind);
    }

    [Fact]
    public async Task ImportFromJson_PreservesPersistedLessonProgress_OnReimport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new StudyHubDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
        }

        var parser = new ExternalCourseJsonParser();
        var service = new ExternalCourseImportService(
            new TestDbContextFactory(options),
            parser,
            NullLogger<ExternalCourseImportService>.Instance);

        var firstImport = await service.ImportFromJsonAsync(BuildSamplePayload("not-started", 0, 0));

        Assert.True(firstImport.Success);
        Assert.NotNull(firstImport.CourseId);

        await using (var updateContext = new StudyHubDbContext(options))
        {
            var lesson = await updateContext.Lessons.SingleAsync();
            lesson.Status = LessonStatus.Completed;
            lesson.WatchedPercentage = 100;
            lesson.LastPlaybackPositionSeconds = 780;
            await updateContext.SaveChangesAsync();
        }

        var secondImport = await service.ImportFromJsonAsync(BuildSamplePayload("not-started", 0, 0));

        Assert.True(secondImport.Success);
        Assert.Equal(studyhub.application.Contracts.ExternalImport.ExternalCourseImportStatus.Updated, secondImport.Status);

        await using var assertContext = new StudyHubDbContext(options);
        var persistedCourse = await assertContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Topics)
                    .ThenInclude(topic => topic.Lessons)
            .SingleAsync();

        var persistedLesson = persistedCourse.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons)
            .Single();

        Assert.Equal(CourseSourceType.ExternalImport, persistedCourse.SourceType);
        Assert.Equal(LessonStatus.Completed, persistedLesson.Status);
        Assert.Equal(100d, persistedLesson.WatchedPercentage);
        Assert.Equal(780, persistedLesson.LastPlaybackPositionSeconds);
        Assert.True(await assertContext.ExternalCourseImports.AnyAsync());
        Assert.Equal(1, await assertContext.ExternalAssessments.CountAsync());
    }

    private static string BuildSamplePayload(string lessonStatus, int watchedPercentage, int lastPositionSeconds)
        =>
        $$"""
        {
          "schemaVersion": "1.0.0",
          "source": {
            "kind": "external-platform-export",
            "system": "studyhub-sync",
            "provider": "univirtus",
            "providerVersion": "1.0.0",
            "exportedAt": "2026-04-15T18:00:00Z",
            "originUrl": "https://ava.exemplo.local/discipline/123",
            "locale": "pt-BR",
            "pageType": "discipline-detail"
          },
          "course": {
            "externalId": "univirtus:course:123",
            "slug": "banco-de-dados-i",
            "title": "Banco de Dados I",
            "description": "Curso externo importado.",
            "sourceType": "external-import",
            "category": "Curso Externo",
            "provider": "univirtus"
          },
          "disciplines": [
            {
              "externalId": "univirtus:discipline:123",
              "code": "123",
              "title": "Banco de Dados I",
              "description": "Disciplina principal",
              "status": "in-progress",
              "modules": [
                {
                  "externalId": "univirtus:discipline:123:module:1",
                  "order": 1,
                  "title": "Modulo 1",
                  "description": "Base relacional",
                  "lessons": [
                    {
                      "externalId": "univirtus:discipline:123:lesson:1",
                      "order": 1,
                      "title": "Videoaula 1",
                      "description": "Introducao",
                      "type": "video",
                      "status": "{{lessonStatus}}",
                      "durationSeconds": 780,
                      "progress": {
                        "watchedPercentage": {{watchedPercentage}},
                        "lastPositionSeconds": {{lastPositionSeconds}}
                      },
                      "source": {
                        "kind": "external-video",
                        "provider": "univirtus",
                        "url": "https://videos.exemplo.local/aula-1"
                      }
                    }
                  ]
                }
              ],
              "assessments": [
                {
                  "externalId": "univirtus:discipline:123:assessment:1",
                  "type": "quiz",
                  "title": "Quiz 1",
                  "description": "Primeira avaliacao",
                  "status": "scheduled",
                  "weightPercentage": 10,
                  "availability": {
                    "startAt": "2026-04-20T00:00:00Z",
                    "endAt": "2026-04-25T23:59:59Z"
                  }
                }
              ]
            }
          ]
        }
        """;

    private sealed class TestDbContextFactory(DbContextOptions<StudyHubDbContext> options) : IDbContextFactory<StudyHubDbContext>
    {
        private readonly DbContextOptions<StudyHubDbContext> _options = options;

        public StudyHubDbContext CreateDbContext()
            => new(_options);

        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new StudyHubDbContext(_options));
    }
}
