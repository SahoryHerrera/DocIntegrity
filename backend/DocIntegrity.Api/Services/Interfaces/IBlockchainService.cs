namespace DocIntegrity.Api.Services.Interfaces;

public interface IBlockchainService
{
    Task<(string TransactionHash, long BlockNumber)> RegisterDocumentAsync(
        string sha256
    );

    Task<bool> IsDocumentRegisteredAsync(
        string sha256
    );

    // ---------------------------------------------------------
    // Consultas de solo lectura (sin transacciones ni gas)
    // ---------------------------------------------------------

    // Dirección del contrato configurado (dato público de la red).
    string ContractAddress { get; }

    Task<long> GetChainIdAsync();

    // getDocument(bytes32) del contrato DocumentRegistry.
    Task<OnChainDocumentRecord> GetDocumentRecordAsync(
        string sha256
    );

    // Busca el recibo de una transacción guardada en PostgreSQL y
    // comprueba que sea el registro de esta huella en este contrato.
    Task<StoredTransactionCheck> CheckRegistrationTransactionAsync(
        string transactionHash,
        string sha256
    );

    // Busca en la cadena activa la transacción que registró esta huella
    // (evento DocumentRegistered) y solo la devuelve si su recibo confirma
    // la ejecución correcta, el contrato, el evento y el bloque.
    // Devuelve null si no hay evidencia confirmable.
    Task<(string TransactionHash, long BlockNumber)?> FindConfirmedRegistrationAsync(
        string sha256
    );
}

public record OnChainDocumentRecord(
    bool Exists,
    DateTime? RegisteredAt,
    string? RegisteredBy
);

public record StoredTransactionCheck(
    // El nodo activo conoce la transacción (tiene recibo).
    bool Found,

    // El recibo indica ejecución correcta (status = 1).
    bool Succeeded,

    // Bloque en el que la incluyó la cadena activa.
    long? BlockNumber,

    // La transacción se envió al contrato configurado.
    bool SentToContract,

    // El recibo contiene el evento DocumentRegistered con esta huella.
    bool EmitsDocumentHash
);
