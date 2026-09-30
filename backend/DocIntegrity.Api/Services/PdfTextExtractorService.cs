using System.Text;
using DocIntegrity.Api.Services.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocIntegrity.Api.Services;

public class PdfTextExtractorService : IDocumentTextExtractorService
{
    public Task<string> ExtractTextAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();

        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);

        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            var pageText = ContentOrderTextExtractor.GetText(page);

            text.AppendLine(pageText);
        }

        return Task.FromResult(text.ToString());
    }
}