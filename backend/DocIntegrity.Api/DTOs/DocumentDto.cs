using DocIntegrity.Api.Models;

namespace DocIntegrity.Api.DTOs;

// Documento tal como lo ve su propietario. Mantiene los nombres de campo que
// devolvía la entidad, pero NO expone RegisteredByUserId ni datos del usuario.
public class DocumentDto
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime RegisteredAt { get; set; }

    public string BlockchainTransactionHash { get; set; } = string.Empty;

    public long BlockchainBlockNumber { get; set; }

    public bool BlockchainEvidenceReused { get; set; }

    public string AiDocumentType { get; set; } = string.Empty;

    public string AiTitle { get; set; } = string.Empty;

    public string AiSummary { get; set; } = string.Empty;

    public string AiMetadataJson { get; set; } = "{}";

    public static DocumentDto From(Document document) => new()
    {
        Id = document.Id,
        FileName = document.FileName,
        Sha256 = document.Sha256,
        Size = document.Size,
        RegisteredAt = document.RegisteredAt,
        BlockchainTransactionHash = document.BlockchainTransactionHash,
        BlockchainBlockNumber = document.BlockchainBlockNumber,
        BlockchainEvidenceReused = document.BlockchainEvidenceReused,
        AiDocumentType = document.AiDocumentType,
        AiTitle = document.AiTitle,
        AiSummary = document.AiSummary,
        AiMetadataJson = document.AiMetadataJson
    };
}
