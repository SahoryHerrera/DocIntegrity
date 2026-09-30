namespace DocIntegrity.Api.DTOs;

public class DocumentAnalysisDto
{
    public string DocumentType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public Dictionary<string, string> Metadata { get; set; } = new();
}