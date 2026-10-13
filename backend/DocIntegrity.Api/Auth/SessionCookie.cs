using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DocIntegrity.Api.Auth;

// Cookie de sesión de Huella: contiene el JWT del sistema y JavaScript no puede leerla.
public static class SessionCookie
{
    public const string Name = "huella_session";

    // Las solicitudes que modifican datos deben enviar esta cabecera (protección CSRF).
    public const string CsrfHeader = "X-Requested-With";
    public const string CsrfHeaderValue = "Huella";

    public static CookieOptions Options(DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        // En desarrollo, http://localhost cuenta como contexto seguro para los navegadores.
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api",
        Expires = expiresAtUtc,
        IsEssential = true
    };

    public static CookieOptions DeleteOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api"
    };
}

public static class ClaimsPrincipalExtensions
{
    // Identificador del usuario del JWT ya validado (claim sub / nameidentifier).
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var value =
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static DateTime? GetExpiresAt(this ClaimsPrincipal user)
    {
        var exp = user.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;

        return long.TryParse(exp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
    }
}
