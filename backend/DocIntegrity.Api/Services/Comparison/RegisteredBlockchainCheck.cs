using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Exceptions;
using DocIntegrity.Api.Models;

namespace DocIntegrity.Api.Services.Comparison;

// Comprobación informativa, de solo lectura, de un documento de la biblioteca en la
// cadena activa. Reutiliza BlockchainStatusReader (el mismo criterio que el detalle del
// documento) y nunca lanza: si la red falla, el resultado es "unavailable".
public static class RegisteredBlockchainCheck
{
    public static class Statuses
    {
        public const string Confirmed = "confirmed";
        public const string NotRegistered = "not_registered";
        public const string Unconfirmed = "unconfirmed";
        public const string Unavailable = "unavailable";
    }

    public static async Task<RegisteredBlockchainCheckDto> RunAsync(
        BlockchainStatusReader reader,
        Document document,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var read = reader.ReadAsync(document);

        try
        {
            return From(await read.WaitAsync(timeout, cancellationToken));
        }
        catch (TimeoutException)
        {
            // La consulta sigue en curso: su posible error se observa para que no quede sin tratar.
            _ = read.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            logger.LogWarning("La consulta a la blockchain superó {Seconds} s durante una comparación.", timeout.TotalSeconds);

            return Unavailable("blockchain_timeout", "La red blockchain tardó demasiado en responder.");
        }
        catch (ExternalServiceException ex)
        {
            logger.LogWarning("No se pudo consultar la blockchain para la comparación: {Error}", ex.Message);

            var code = ex.Failure switch
            {
                ExternalFailure.Timeout => "blockchain_timeout",
                ExternalFailure.Rejected => "blockchain_rejected",
                _ => "blockchain_unavailable"
            };

            return Unavailable(code, ex.Message);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Configuración incompleta u otro fallo del cliente: no se sabe nada de la cadena.
            logger.LogWarning(ex, "No se pudo consultar la blockchain para la comparación.");

            return Unavailable("blockchain_misconfigured",
                "No se pudo consultar la blockchain. Revisa la configuración del nodo y del contrato.");
        }
    }

    // confirmed exige las dos evidencias: huella en el contrato y transacción guardada confirmada.
    public static RegisteredBlockchainCheckDto From(BlockchainStatusDto status)
    {
        var (code, message) =
            !status.RegisteredInBlockchain
                ? (Statuses.NotRegistered,
                   "El contrato de la blockchain activa no tiene esta huella. Puede pertenecer a una cadena anterior (por ejemplo, si el nodo se reinició).")
                : status.StoredTransactionConfirmed
                    ? (Statuses.Confirmed,
                       "La huella está en el contrato y la transacción guardada se confirmó en la blockchain activa.")
                    : (Statuses.Unconfirmed,
                       "La huella está en el contrato, pero la transacción guardada en PostgreSQL no se pudo confirmar en la blockchain activa.");

        return new RegisteredBlockchainCheckDto
        {
            Status = code,
            Message = message,
            Detail = status,
            CheckedAt = status.CheckedAt
        };
    }

    public static RegisteredBlockchainCheckDto Unavailable(string failureCode, string message) => new()
    {
        Status = Statuses.Unavailable,
        FailureCode = failureCode,
        Message = $"{message} La comparación se hizo igualmente; no se pudo comprobar la evidencia en la blockchain.",
        CheckedAt = DateTime.UtcNow
    };
}
