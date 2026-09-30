namespace DocIntegrity.Api.Services.Interfaces;

public interface IDocumentTextExtractorService
{
    Task<string> ExtractTextAsync(Stream stream);
}