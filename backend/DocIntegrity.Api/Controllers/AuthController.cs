using DocIntegrity.Api.Auth;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Exceptions;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocIntegrity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext dbContext,
        IJwtService jwtService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _configuration = configuration;
        _logger = logger;
    }

    // =========================================================
    // INICIO DE SESIÓN CON GOOGLE
    // El ID token se valida con Google (firma, emisor, audiencia
    // y expiración) y se emite el JWT de Huella en una cookie HttpOnly.
    // =========================================================

    [AllowAnonymous]
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponseDto>> GoogleLogin(
        [FromBody] GoogleLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return BadRequest(new
            {
                message = "El token de Google es obligatorio."
            });
        }

        var googleClientId =
            _configuration["Google:ClientId"];

        if (string.IsNullOrWhiteSpace(googleClientId))
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message =
                        "Google Client ID no está configurado."
                }
            );
        }

        GoogleJsonWebSignature.Payload payload;

        try
        {
            var settings =
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[]
                    {
                        googleClientId
                    }
                };

            payload =
                await GoogleJsonWebSignature.ValidateAsync(
                    request.IdToken,
                    settings
                );
        }
        catch (InvalidJwtException ex)
        {
            // Token falso, vencido, con otra audiencia o firma incorrecta.
            // Solo se registra el tipo de fallo, nunca el token.
            _logger.LogWarning("ID token de Google rechazado: {Reason}", ex.Message);

            return Unauthorized(new
            {
                message =
                    "El token de Google no es válido."
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // No se pudieron descargar las claves públicas de Google.
            throw new ExternalServiceException(
                ExternalService.Identity,
                ExternalFailure.Unavailable,
                "No se pudo validar la identidad con Google. Intenta de nuevo más tarde.",
                ex
            );
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
        {
            return Unauthorized(new
            {
                message =
                    "Tu cuenta de Google no tiene un correo electrónico verificado."
            });
        }

        // La identidad es el identificador estable de Google (sub), nunca el correo.
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(
                user => user.GoogleId == payload.Subject
            );

        if (user is null)
        {
            // No se vinculan cuentas por coincidencia de correo.
            var emailTaken = await _dbContext.Users
                .AnyAsync(user => user.Email == payload.Email);

            if (emailTaken)
            {
                return Conflict(new
                {
                    message =
                        "Ya existe una cuenta de Huella con este correo vinculada a otra identidad de Google. Contacta con el administrador.",
                    code = "email_linked_to_other_identity"
                });
            }

            user = new User
            {
                Id = Guid.NewGuid(),
                GoogleId = payload.Subject,
                Email = payload.Email,
                Name = payload.Name ?? payload.Email,
                PictureUrl = payload.Picture ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
        }
        else
        {
            user.Email = payload.Email;
            user.Name = payload.Name ?? user.Name;
            user.PictureUrl = payload.Picture ?? user.PictureUrl;
            user.LastLoginAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        var jwt = _jwtService.GenerateToken(user);

        // Los navegadores reciben el JWT solo en la cookie HttpOnly.
        if (!request.ReturnToken)
        {
            Response.Cookies.Append(
                SessionCookie.Name,
                jwt.Token,
                SessionCookie.Options(jwt.ExpiresAt)
            );
        }

        return Ok(new AuthResponseDto
        {
            Token = request.ReturnToken ? jwt.Token : string.Empty,
            ExpiresAt = jwt.ExpiresAt,
            User = ToDto(user)
        });
    }

    // =========================================================
    // SESIÓN ACTUAL
    // =========================================================

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<SessionDto>> GetCurrentUser()
    {
        var userId = User.GetUserId();

        var user = userId is null
            ? null
            : await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == userId);

        // El JWT es válido, pero su usuario ya no existe.
        if (user is null)
        {
            return Unauthorized(new
            {
                message = "Tu sesión no es válida. Inicia sesión de nuevo.",
                code = "unauthorized"
            });
        }

        return Ok(new SessionDto
        {
            User = ToDto(user),
            ExpiresAt = User.GetExpiresAt() ?? DateTime.UtcNow,
            SessionMinutes = _jwtService.ExpirationMinutes
        });
    }

    // =========================================================
    // CIERRE DE SESIÓN
    // Público para poder borrar también una cookie ya vencida.
    // =========================================================

    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(
            SessionCookie.Name,
            SessionCookie.DeleteOptions()
        );

        return NoContent();
    }

    private static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        Name = user.Name,
        PictureUrl = user.PictureUrl
    };
}
