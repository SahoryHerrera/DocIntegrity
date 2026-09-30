using DocIntegrity.Api.DTOs;

namespace DocIntegrity.Api.Services.Interfaces;

public interface IAiService
{
    Task<DocumentAnalysisDto> AnalyzeDocumentAsync(
        string documentText
    );
}