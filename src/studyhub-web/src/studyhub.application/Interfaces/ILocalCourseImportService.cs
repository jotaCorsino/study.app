using studyhub.application.Contracts.LocalImport;

namespace studyhub.application.Interfaces;

public interface ILocalCourseImportService
{
    Task<LocalCourseImportResult> PickAndImportAsync(CancellationToken cancellationToken = default);
    Task<LocalCourseImportResult> ImportFromFolderAsync(string folderPath, CancellationToken cancellationToken = default);
}
