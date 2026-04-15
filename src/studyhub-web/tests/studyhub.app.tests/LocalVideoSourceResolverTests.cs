using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using studyhub.app.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class LocalVideoSourceResolverTests : IDisposable
{
    private readonly string _rootDirectory;

    public LocalVideoSourceResolverTests()
    {
        _rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "studyhub-local-media-tests",
            Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task ResolvePlaybackUrl_ServesFileBytes_ForPathsWithSpacesAndHash()
    {
        Directory.CreateDirectory(_rootDirectory);

        var courseRoot = Path.Combine(_rootDirectory, "Curso Balta io");
        var nestedFolder = Path.Combine(
            courseRoot,
            "01 .Net Developer Fundamentals",
            "01-Fundamentos do C#",
            "01 Linguagens e Compiladores");
        Directory.CreateDirectory(nestedFolder);

        var filePath = Path.Combine(nestedFolder, "M01A01 - Apresentacao_1080p.mp4");
        var expectedBytes = CreateFakeMp4Payload();
        await File.WriteAllBytesAsync(filePath, expectedBytes);

        using var resolver = new LocalVideoSourceResolver(NullLogger<LocalVideoSourceResolver>.Instance);

        var playbackUrl = resolver.ResolvePlaybackUrl(Guid.NewGuid(), courseRoot, filePath);

        Assert.False(string.IsNullOrWhiteSpace(playbackUrl));

        using var client = new HttpClient();
        var actualBytes = await client.GetByteArrayAsync(playbackUrl);

        Assert.Equal(expectedBytes, actualBytes);
    }

    [Fact]
    public async Task ResolvePlaybackUrl_SupportsRangeRequests()
    {
        Directory.CreateDirectory(_rootDirectory);

        var courseRoot = Path.Combine(_rootDirectory, "Curso Range Test");
        Directory.CreateDirectory(courseRoot);

        var filePath = Path.Combine(courseRoot, "M01A02 - Aula com espaco.mp4");
        var expectedBytes = CreateFakeMp4Payload();
        await File.WriteAllBytesAsync(filePath, expectedBytes);

        using var resolver = new LocalVideoSourceResolver(NullLogger<LocalVideoSourceResolver>.Instance);
        var playbackUrl = resolver.ResolvePlaybackUrl(Guid.NewGuid(), courseRoot, filePath);

        Assert.False(string.IsNullOrWhiteSpace(playbackUrl));

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, playbackUrl);
        request.Headers.Range = new RangeHeaderValue(4, 15);

        using var response = await client.SendAsync(request);
        var partialBytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(expectedBytes[4..16], partialBytes);
    }

    [Fact]
    public void ResolvePlaybackUrl_ReturnsNull_ForFilesOutsideCourseRoot()
    {
        Directory.CreateDirectory(_rootDirectory);

        var courseRoot = Path.Combine(_rootDirectory, "Curso Seguro");
        var outsideFolder = Path.Combine(_rootDirectory, "Outro Curso");
        Directory.CreateDirectory(courseRoot);
        Directory.CreateDirectory(outsideFolder);

        var outsideFile = Path.Combine(outsideFolder, "M01A03 - Externo.mp4");
        File.WriteAllBytes(outsideFile, CreateFakeMp4Payload());

        using var resolver = new LocalVideoSourceResolver(NullLogger<LocalVideoSourceResolver>.Instance);

        var playbackUrl = resolver.ResolvePlaybackUrl(Guid.NewGuid(), courseRoot, outsideFile);

        Assert.Null(playbackUrl);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return;
        }

        Directory.Delete(_rootDirectory, recursive: true);
    }

    private static byte[] CreateFakeMp4Payload()
    {
        return
        [
            0x00, 0x00, 0x00, 0x20,
            0x66, 0x74, 0x79, 0x70,
            0x69, 0x73, 0x6F, 0x6D,
            0x00, 0x00, 0x02, 0x00,
            0x69, 0x73, 0x6F, 0x6D,
            0x69, 0x73, 0x6F, 0x32,
            0x61, 0x76, 0x63, 0x31,
            0x6D, 0x70, 0x34, 0x31,
            0x00, 0x00, 0x00, 0x08,
            0x66, 0x72, 0x65, 0x65,
            0x00, 0x00, 0x00, 0x0C,
            0x6D, 0x64, 0x61, 0x74,
            0x01, 0x02, 0x03, 0x04
        ];
    }
}
