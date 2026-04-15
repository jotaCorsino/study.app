namespace studyhub.application.Interfaces;

public interface IVideoMetadataReader
{
    Task<TimeSpan?> TryReadDurationAsync(string filePath, CancellationToken cancellationToken = default);
}
