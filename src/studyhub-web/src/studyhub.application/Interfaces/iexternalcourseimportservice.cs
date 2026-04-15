using studyhub.application.Contracts.ExternalImport;

namespace studyhub.application.Interfaces;

public interface IExternalCourseImportService
{
    Task<ExternalCourseImportResult> ImportFromJsonAsync(string json, CancellationToken cancellationToken = default);
}
