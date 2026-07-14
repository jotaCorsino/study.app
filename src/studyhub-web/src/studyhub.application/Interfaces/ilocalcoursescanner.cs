using studyhub.application.Contracts.LocalImport;

namespace studyhub.application.Interfaces;

public interface ILocalCourseScanner
{
    Task<DetectedCourseStructure> ScanAsync(
        string rootFolderPath,
        CancellationToken cancellationToken = default);
}
