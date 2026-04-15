using studyhub.application.Interfaces;

namespace studyhub.app.services;

public class VideoMetadataReader : IVideoMetadataReader
{
    public async Task<TimeSpan?> TryReadDurationAsync(string filePath, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        try
        {
            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            var properties = await storageFile.Properties.GetVideoPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return properties.Duration;
        }
        catch
        {
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
