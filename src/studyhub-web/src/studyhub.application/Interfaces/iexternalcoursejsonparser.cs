using studyhub.application.Contracts.ExternalImport;

namespace studyhub.application.Interfaces;

public interface IExternalCourseJsonParser
{
    ExternalCourseImportParseResult Parse(string json);
}
