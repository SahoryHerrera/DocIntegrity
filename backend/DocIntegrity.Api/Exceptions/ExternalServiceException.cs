namespace DocIntegrity.Api.Exceptions;

public enum ExternalService
{
    Ai,
    Blockchain
}

public enum ExternalFailure
{
    // El servicio no respondió o no se pudo conectar.
    Unavailable,

    // El servicio no respondió dentro del tiempo de espera.
    Timeout,

    // El servicio respondió, pero rechazó la operación o devolvió algo inválido.
    Rejected
}

// Error de un servicio externo (Ollama o el nodo blockchain).
// Message es seguro para mostrarse al usuario; el detalle técnico va en InnerException.
public class ExternalServiceException : Exception
{
    public ExternalService Service { get; }

    public ExternalFailure Failure { get; }

    public ExternalServiceException(
        ExternalService service,
        ExternalFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Service = service;
        Failure = failure;
    }
}

// El contrato rechazó el registro porque la huella ya existe en la cadena activa.
public class DocumentAlreadyOnChainException : Exception
{
    public DocumentAlreadyOnChainException(Exception? innerException = null)
        : base(
            "La huella de este documento ya está registrada en la blockchain activa.",
            innerException)
    {
    }
}
