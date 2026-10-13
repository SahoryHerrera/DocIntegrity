using DocIntegrity.Api.Auth;

namespace DocIntegrity.Api.Middleware;

// Protección CSRF para la sesión en cookie.
// Las solicitudes que modifican datos deben:
//   1. Venir de un origen permitido (si el navegador envía Origin).
//   2. Llevar la cabecera X-Requested-With: Huella, que un formulario o un
//      enlace de otro sitio no pueden añadir, y que en fetch de otro origen
//      obliga a una consulta CORS previa que se rechaza.
// Los clientes que usan Authorization: Bearer no dependen de la cookie y quedan exentos.
public class CsrfProtectionMiddleware
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    private readonly RequestDelegate _next;
    private readonly string[] _allowedOrigins;

    public CsrfProtectionMiddleware(RequestDelegate next, string[] allowedOrigins)
    {
        _next = next;
        _allowedOrigins = allowedOrigins;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        var isSafe = SafeMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase);
        var usesBearer = request.Headers.Authorization.ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

        if (isSafe || usesBearer)
        {
            await _next(context);
            return;
        }

        var origin = request.Headers.Origin.ToString();
        var originAllowed = string.IsNullOrEmpty(origin) ||
            _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);

        var headerOk = string.Equals(
            request.Headers[SessionCookie.CsrfHeader].ToString(),
            SessionCookie.CsrfHeaderValue,
            StringComparison.Ordinal);

        if (!originAllowed || !headerOk)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            await context.Response.WriteAsJsonAsync(new
            {
                message = "La solicitud fue rechazada por la protección contra CSRF.",
                code = "csrf_check_failed"
            });

            return;
        }

        await _next(context);
    }
}
