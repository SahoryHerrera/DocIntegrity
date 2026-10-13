namespace DocIntegrity.Api.DTOs;

// Estado de un documento en la blockchain ACTIVA, consultado en el momento.
// Separa dos preguntas distintas:
//   1. ¿Está la huella en el contrato? (registeredInBlockchain, getDocument)
//   2. ¿La transacción que guardó PostgreSQL existe y es ese registro?
//      (storedTransaction, recibo de la transacción)
public class BlockchainStatusDto
{
    public Guid DocumentId { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public long NetworkChainId { get; set; }

    public string ContractAddress { get; set; } = string.Empty;

    // La huella existe en el contrato de la cadena activa.
    public bool RegisteredInBlockchain { get; set; }

    // Fecha del bloque en que el contrato registró la huella (no la de PostgreSQL).
    public DateTime? OnChainRegisteredAt { get; set; }

    public string? OnChainRegisteredBy { get; set; }

    // Datos guardados en PostgreSQL al registrar.
    public string? StoredTransactionHash { get; set; }

    public long? StoredBlockNumber { get; set; }

    // true solo si la transacción guardada existe en la cadena activa, se
    // ejecutó correctamente, fue al contrato configurado, emitió el evento
    // con esta huella y está en el mismo bloque guardado.
    public bool StoredTransactionConfirmed { get; set; }

    public StoredTransactionStatusDto StoredTransaction { get; set; } = new();

    public DateTime CheckedAt { get; set; }
}

public class StoredTransactionStatusDto
{
    // not_stored | not_found | failed | mismatch | confirmed
    public string Status { get; set; } = "not_stored";

    public bool FoundInActiveChain { get; set; }

    public bool? Succeeded { get; set; }

    public long? ActiveChainBlockNumber { get; set; }

    public bool? SentToContract { get; set; }

    public bool? EmitsDocumentHash { get; set; }
}
