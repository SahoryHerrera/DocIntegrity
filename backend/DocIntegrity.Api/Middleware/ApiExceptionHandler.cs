using DocIntegrity.Api.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace DocIntegrity.Api.Middleware;

// Convierte cualquier excepción no controlada en una respuesta JSON segura:
// { message, code, traceId }. El detalle técnico solo se escribe en el log.
public class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // El cliente cerró la conexión: no hay a quién responder.
        if (exception is OperationCanceledException &&
            httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Solicitud cancelada por el cliente: {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);

            return true;
        }

        var (status, code, message) = Classify(exception);

        if (status >= 500)
        {
            _logger.LogError(
                exception,
                "Error {Code} en {Method} {Path}",
                code,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                "Solicitud rechazada ({Code}) en {Method} {Path}: {Error}",
                code,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        httpContext.Response.StatusCode = status;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                message,
                code,
                traceId = httpContext.TraceIdentifier
            },
            cancellationToken);

        return true;
    }

    private static (int Status, string Code, string Message) Classify(
        Exception exception)
    {
        switch (exception)
        {
            case DocumentAlreadyOnChainException:
                return (
                    StatusCodes.Status409Conflict,
                    "blockchain_conflict",
                    exception.Message);

            case ExternalServiceException external:
                return Classify(external);

            case BadHttpRequestException badRequest
                when badRequest.StatusCode ==
                     StatusCodes.Status413PayloadTooLarge:
                return (
                    StatusCodes.Status413PayloadTooLarge,
                    "file_too_large",
                    "El archivo supera el tamaño máximo que acepta el servidor.");

            case BadHttpRequestException:
                return (
                    StatusCodes.Status400BadRequest,
                    "bad_request",
                    "La solicitud no es válida.");
        }

        if (FindInChain<NpgsqlException>(exception) is { } npgsql)
        {
            // PostgresException: el servidor respondió con un error de SQL.
            // Cualquier otra NpgsqlException: no se pudo conectar o se perdió la conexión.
            return npgsql is PostgresException
                ? (
                    StatusCodes.Status500InternalServerError,
                    "database_error",
                    "La base de datos no pudo completar la operación.")
                : (
                    StatusCodes.Status503ServiceUnavailable,
                    "database_unavailable",
                    "La base de datos PostgreSQL no está disponible. Intenta de nuevo más tarde.");
        }

        return (
            StatusCodes.Status500InternalServerError,
            "internal_error",
            "Ocurrió un error inesperado en el servidor.");
    }

    private static (int Status, string Code, string Message) Classify(
        ExternalServiceException exception)
    {
        var service = exception.Service switch
        {
            ExternalService.Ai => "ai",
            ExternalService.Identity => "identity",
            _ => "blockchain"
        };

        return exception.Failure switch
        {
            ExternalFailure.Timeout => (
                StatusCodes.Status504GatewayTimeout,
                $"{service}_timeout",
                exception.Message),

            ExternalFailure.Rejected => (
                StatusCodes.Status502BadGateway,
                $"{service}_rejected",
                exception.Message),

            _ => (
                StatusCodes.Status503ServiceUnavailable,
                $"{service}_unavailable",
                exception.Message)
        };
    }

    private static T? FindInChain<T>(Exception exception)
        where T : Exception
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
