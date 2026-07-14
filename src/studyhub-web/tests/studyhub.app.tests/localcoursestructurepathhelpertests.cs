using studyhub.infrastructure.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class LocalCourseStructurePathHelperTests
{
    [Theory]
    [InlineData(".", ".")]
    [InlineData("  .  ", ".")]
    [InlineData("Modulo 01", "Modulo 01")]
    [InlineData("Modulo 01\\Topico 01", "Modulo 01/Topico 01")]
    [InlineData("Modulo 01//Topico 01///Subtopico", "Modulo 01/Topico 01/Subtopico")]
    public void TryNormalize_ReturnsPortableStructuralPath(string path, string expected)
    {
        var result = LocalCourseStructurePathHelper.TryNormalize(path, out var normalizedPath);

        Assert.True(result);
        Assert.Equal(expected, normalizedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/Modulo 01")]
    [InlineData("C:\\Cursos\\Modulo 01")]
    [InlineData("\\\\servidor\\Cursos\\Modulo 01")]
    [InlineData("../Modulo 01")]
    [InlineData("Modulo 01/../Topico 01")]
    [InlineData("Modulo 01/./Topico 01")]
    [InlineData("./")]
    public void TryNormalize_RejectsUnsafeOrNonCanonicalRootPath(string path)
    {
        var result = LocalCourseStructurePathHelper.TryNormalize(path, out var normalizedPath);

        Assert.False(result);
        Assert.Equal(string.Empty, normalizedPath);
    }

    [Theory]
    [InlineData(".", ".", ".")]
    [InlineData(".", "Topico 01", "Topico 01")]
    [InlineData("Modulo 01", ".", "Modulo 01")]
    [InlineData("Modulo 01", "Topico 01", "Modulo 01/Topico 01")]
    [InlineData("Modulo 01\\Submodulo", "Topico 01\\Parte 01", "Modulo 01/Submodulo/Topico 01/Parte 01")]
    public void TryCombine_ComposesRootRelativeStructuralPath(
        string parentPath,
        string childPath,
        string expected)
    {
        var result = LocalCourseStructurePathHelper.TryCombine(
            parentPath,
            childPath,
            out var combinedPath);

        Assert.True(result);
        Assert.Equal(expected, combinedPath);
    }

    [Fact]
    public void TryCombine_RejectsUnsafeChild()
    {
        var result = LocalCourseStructurePathHelper.TryCombine(
            "Modulo 01",
            "../Outro Modulo",
            out var combinedPath);

        Assert.False(result);
        Assert.Equal(string.Empty, combinedPath);
    }

    [Theory]
    [InlineData("Aula 01.mp4", ".")]
    [InlineData("Modulo 01/Aula 01.mp4", "Modulo 01")]
    [InlineData("Modulo 01\\Topico 01\\Aula 01.mp4", "Modulo 01/Topico 01")]
    public void TryGetParentDirectoryFromLessonPath_ReturnsStructuralOrigin(
        string lessonPath,
        string expected)
    {
        var result = LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
            lessonPath,
            out var parentPath);

        Assert.True(result);
        Assert.Equal(expected, parentPath);
    }

    [Fact]
    public void TryGetParentDirectoryFromLessonPath_RejectsTraversal()
    {
        var result = LocalCourseStructurePathHelper.TryGetParentDirectoryFromLessonPath(
            "../Aula 01.mp4",
            out var parentPath);

        Assert.False(result);
        Assert.Equal(string.Empty, parentPath);
    }

    [Theory]
    [MemberData(nameof(CommonAncestorCases))]
    public void TryGetCommonAncestor_ReturnsPortableAncestor(
        string[] paths,
        string expected)
    {
        var result = LocalCourseStructurePathHelper.TryGetCommonAncestor(
            paths,
            out var commonAncestor);

        Assert.True(result);
        Assert.Equal(expected, commonAncestor);
    }

    [Fact]
    public void TryGetCommonAncestor_RejectsEmptyOrUnsafeInput()
    {
        Assert.False(LocalCourseStructurePathHelper.TryGetCommonAncestor(
            [],
            out var emptyAncestor));
        Assert.Equal(string.Empty, emptyAncestor);

        Assert.False(LocalCourseStructurePathHelper.TryGetCommonAncestor(
            ["Modulo 01", "../Outro Modulo"],
            out var unsafeAncestor));
        Assert.Equal(string.Empty, unsafeAncestor);
    }

    [Theory]
    [InlineData(".", ".", ".")]
    [InlineData(".", "Modulo 01/Topico 01", "Modulo 01/Topico 01")]
    [InlineData("Modulo 01", "Modulo 01", ".")]
    [InlineData("Modulo 01", "Modulo 01/Topico 01", "Topico 01")]
    [InlineData("Modulo 01\\Submodulo", "Modulo 01/Submodulo/Topico 01", "Topico 01")]
    public void TryMakeRelativeToParent_ReturnsChildRelativePath(
        string parentPath,
        string childRootPath,
        string expected)
    {
        var result = LocalCourseStructurePathHelper.TryMakeRelativeToParent(
            parentPath,
            childRootPath,
            out var childRelativePath);

        Assert.True(result);
        Assert.Equal(expected, childRelativePath);
    }

    [Theory]
    [InlineData("Modulo 01", ".")]
    [InlineData("Modulo 01", "Modulo 02/Topico 01")]
    [InlineData("Modulo 01/Topico", "Modulo 01")]
    public void TryMakeRelativeToParent_RejectsPathOutsideParent(
        string parentPath,
        string childRootPath)
    {
        var result = LocalCourseStructurePathHelper.TryMakeRelativeToParent(
            parentPath,
            childRootPath,
            out var childRelativePath);

        Assert.False(result);
        Assert.Equal(string.Empty, childRelativePath);
    }

    public static TheoryData<string[], string> CommonAncestorCases => new()
    {
        { ["Modulo 01/Topico A", "Modulo 01/Topico B"], "Modulo 01" },
        { ["Modulo 01", "Modulo 01/Topico 01"], "Modulo 01" },
        { ["Modulo 01", "Modulo 02"], "." },
        { [".", "Modulo 01"], "." },
        { ["Modulo 01/Topico 01"], "Modulo 01/Topico 01" }
    };
}
