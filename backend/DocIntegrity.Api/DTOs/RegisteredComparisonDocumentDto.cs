namespace DocIntegrity.Api.DTOs;

// Documento de la biblioteca con el que se comparó una nueva versión
// (solo en POST /api/comparisons/documents/{id}; null en la comparación libre).
// Responde a dos preguntas distintas que no deben confundirse:
//   1. ¿El original enviado es el archivo registrado? (OriginalMatchesRegisteredSha256, contra PostgreSQL)
//   2. ¿Esa huella tiene evidencia en la blockchain activa? (Blockchain, consulta informativa)
public class RegisteredComparisonDocumentDto
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; }

    // El SHA-256 del original enviado coincide con la huella guardada en PostgreSQL.
    // Si no coincidiera no habría comparación (422 original_hash_mismatch), así que
    // siempre es true. NO implica que la huella esté en la blockchain.
    public bool OriginalMatchesRegisteredSha256 { get; set; }

    public RegisteredBlockchainCheckDto Blockchain { get; set; } = new();
}

// Comprobación de solo lectura en la cadena activa. Nunca impide la comparación.
public class RegisteredBlockchainCheckDto
{
    // confirmed: la huella está en el contrato Y la transacción guardada en PostgreSQL
    //            existe en la cadena activa, se ejecutó, fue al contrato, emitió el evento
    //            con esta huella y está en el bloque guardado.
    // not_registered: el contrato de la cadena activa no tiene esta huella.
    // unconfirmed: la huella está en el contrato, pero la transacción guardada no se pudo
    //              confirmar (no existe, falló, no corresponde o no hay ninguna guardada).
    // unavailable: la red no respondió o rechazó la consulta; no se sabe.
    public string Status { get; set; } = "unavailable";

    public string Message { get; set; } = string.Empty;

    // Con Status = unavailable: blockchain_unavailable | blockchain_timeout |
    // blockchain_rejected | blockchain_misconfigured.
    public string? FailureCode { get; set; }

    // Detalle de la consulta, igual que GET /api/documents/{id}/blockchain-status.
    // null si la red no respondió.
    public BlockchainStatusDto? Detail { get; set; }

    public DateTime CheckedAt { get; set; }
}
