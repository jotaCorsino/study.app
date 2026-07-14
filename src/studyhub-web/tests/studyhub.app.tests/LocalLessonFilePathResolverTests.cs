using studyhub.domain.Entities;
using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class LocalLessonFilePathResolverTests
{
    private readonly LocalLessonFilePathResolver _resolver = new();

    [Fact]
    public void Resolve_CombinesCourseRootAndRelativeFilePath()
    {
        var courseRoot = CreateCourseRoot("valid-relative");
        var lesson = new Lesson { RelativeFilePath = "Modulo 01/Aula 01.mp4" };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(courseRoot, "Modulo 01", "Aula 01.mp4")),
            result);
    }

    [Fact]
    public void Resolve_PrefersCurrentCourseRootOverStaleLocalFilePath()
    {
        var currentCourseRoot = CreateCourseRoot("current-root");
        var staleCourseRoot = CreateCourseRoot("stale-root");
        var lesson = new Lesson
        {
            RelativeFilePath = "Modulo 01/Aula 01.mp4",
            LocalFilePath = Path.Combine(staleCourseRoot, "Modulo 01", "Aula 01.mp4")
        };

        var result = _resolver.Resolve(currentCourseRoot, lesson);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(currentCourseRoot, "Modulo 01", "Aula 01.mp4")),
            result);
    }

    [Fact]
    public void Resolve_AcceptsPortableForwardSlashSeparators()
    {
        var courseRoot = CreateCourseRoot("portable-separators");
        var lesson = new Lesson
        {
            RelativeFilePath = "Modulo 01/Topico 01/Aula 01.mp4"
        };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(courseRoot, "Modulo 01", "Topico 01", "Aula 01.mp4")),
            result);
    }

    [Fact]
    public void Resolve_AcceptsCurrentPlatformSeparators()
    {
        var courseRoot = CreateCourseRoot("platform-separators");
        var lesson = new Lesson
        {
            RelativeFilePath = Path.Combine("Modulo 02", "Topico 02", "Aula 02.mp4")
        };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(courseRoot, "Modulo 02", "Topico 02", "Aula 02.mp4")),
            result);
    }

    [Fact]
    public void Resolve_FallsBackToLocalFilePath()
    {
        var legacyPath = Path.Combine(CreateCourseRoot("local-fallback"), "Aula.mp4");
        var lesson = new Lesson { LocalFilePath = legacyPath };

        var result = _resolver.Resolve(courseRootPath: null, lesson);

        Assert.Equal(legacyPath, result);
    }

    [Fact]
    public void Resolve_RemainsCompatibleWithFilePathAlias()
    {
        var legacyPath = Path.Combine(CreateCourseRoot("file-path-fallback"), "Aula.mp4");
        var lesson = new Lesson { FilePath = legacyPath };

        var result = _resolver.Resolve(courseRootPath: null, lesson);

        Assert.Equal(legacyPath, result);
        Assert.Equal(legacyPath, lesson.LocalFilePath);
    }

    [Fact]
    public void Resolve_RejectsParentTraversalAndUsesLegacyFallback()
    {
        var courseRoot = CreateCourseRoot("parent-traversal");
        var legacyPath = Path.Combine(courseRoot, "Legacy", "Aula.mp4");
        var lesson = new Lesson
        {
            RelativeFilePath = "../OutroCurso/Aula.mp4",
            LocalFilePath = legacyPath
        };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(legacyPath, result);
    }

    [Fact]
    public void Resolve_RejectsNestedParentTraversalAndUsesLegacyFallback()
    {
        var courseRoot = CreateCourseRoot("nested-parent-traversal");
        var legacyPath = Path.Combine(courseRoot, "Legacy", "Aula.mp4");
        var lesson = new Lesson
        {
            RelativeFilePath = "../../OutroCurso/Aula.mp4",
            LocalFilePath = legacyPath
        };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(legacyPath, result);
    }

    [Fact]
    public void Resolve_RejectsAbsoluteRelativeFilePathAndUsesLegacyFallback()
    {
        var courseRoot = CreateCourseRoot("absolute-relative");
        var legacyPath = Path.Combine(courseRoot, "Legacy", "Aula.mp4");
        var absoluteOutsidePath = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "studyhub-outside-course",
            Guid.NewGuid().ToString("N"),
            "Aula.mp4"));
        var lesson = new Lesson
        {
            RelativeFilePath = absoluteOutsidePath,
            LocalFilePath = legacyPath
        };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(legacyPath, result);
    }

    [Theory]
    [InlineData(@"C:\OutroLugar\Aula.mp4")]
    [InlineData("D:/OutroLugar/Aula.mp4")]
    [InlineData(@"\\servidor\pasta\Aula.mp4")]
    public void Resolve_RejectsWindowsRootedFormsOnAnyPlatform(string unsafeRelativeFilePath)
    {
        var courseRoot = CreateCourseRoot("windows-rooted");
        var legacyPath = Path.Combine(courseRoot, "Legacy", "Aula.mp4");
        var lesson = new Lesson
        {
            RelativeFilePath = unsafeRelativeFilePath,
            LocalFilePath = legacyPath
        };

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(legacyPath, result);
    }

    [Fact]
    public void Resolve_ReturnsEmptyWhenNoPathIsUsable()
    {
        var lesson = new Lesson();

        var result = _resolver.Resolve(CreateCourseRoot("empty"), lesson);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Resolve_UsesLegacyFallbackWhenCourseRootIsInvalid()
    {
        var legacyPath = Path.Combine(CreateCourseRoot("invalid-root-fallback"), "Aula.mp4");
        var lesson = new Lesson
        {
            RelativeFilePath = "Modulo 01/Aula.mp4",
            LocalFilePath = legacyPath
        };

        var result = _resolver.Resolve(Path.Combine("relative", "course-root"), lesson);

        Assert.Equal(legacyPath, result);
    }

    [Fact]
    public void Resolve_DoesNotRequireThePhysicalFileToExist()
    {
        var courseRoot = CreateCourseRoot("disconnected-storage");
        var lesson = new Lesson { RelativeFilePath = "Modulo 03/Aula inexistente.mp4" };
        var expectedPath = Path.GetFullPath(Path.Combine(
            courseRoot,
            "Modulo 03",
            "Aula inexistente.mp4"));

        Assert.False(File.Exists(expectedPath));

        var result = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal(expectedPath, result);
        Assert.False(File.Exists(result));
    }

    [Fact]
    public void Resolve_DoesNotMutateLessonPaths()
    {
        var courseRoot = CreateCourseRoot("non-mutating");
        var legacyPath = Path.Combine(CreateCourseRoot("non-mutating-legacy"), "Aula.mp4");
        var lesson = new Lesson
        {
            RelativeFilePath = "Modulo 04/Aula 04.mp4",
            LocalFilePath = legacyPath
        };

        _ = _resolver.Resolve(courseRoot, lesson);

        Assert.Equal("Modulo 04/Aula 04.mp4", lesson.RelativeFilePath);
        Assert.Equal(legacyPath, lesson.LocalFilePath);
        Assert.Equal(legacyPath, lesson.FilePath);
    }

    private static string CreateCourseRoot(string scenario)
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "studyhub-local-lesson-path-resolver-tests",
            scenario,
            Guid.NewGuid().ToString("N")));
    }
}
