namespace DocIntegrity.Api.Models;

public class Document
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime RegisteredAt { get; set; }

    public string BlockchainTransactionHash { get; set; } = string.Empty;

    public long BlockchainBlockNumber { get; set; }

    public string AiDocumentType { get; set; } = string.Empty;

    public string AiTitle { get; set; } = string.Empty;

    public string AiSummary { get; set; } = string.Empty;

    public string AiMetadataJson { get; set; } = "{}";
}