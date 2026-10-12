using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
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

    public AuthController(
        AppDbContext dbContext,
        IJwtService jwtService,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _configuration = configuration;
    }

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
        catch
        {
            return Unauthorized(new
            {
                message =
                    "El token de Google no es válido."
            });
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            return Unauthorized(new
            {
                message =
                    "Google no proporcionó un correo electrónico válido."
            });
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(
                user =>
                    user.GoogleId == payload.Subject ||
                    user.Email == payload.Email
            );

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),

                GoogleId =
                    payload.Subject,

                Email =
                    payload.Email,

                Name =
                    payload.Name ?? payload.Email,

                PictureUrl =
                    payload.Picture ?? string.Empty,

                CreatedAt =
                    DateTime.UtcNow,

                LastLoginAt =
                    DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
        }
        else
        {
            user.GoogleId =
                payload.Subject;

            user.Email =
                payload.Email;

            user.Name =
                payload.Name ?? user.Name;

            user.PictureUrl =
                payload.Picture ?? user.PictureUrl;

            user.LastLoginAt =
                DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        var jwt =
            _jwtService.GenerateToken(user);

        var response = new AuthResponseDto
        {
            Token =
                jwt.Token,

            ExpiresAt =
                jwt.ExpiresAt,

            User = new UserDto
            {
                Id =
                    user.Id,

                Email =
                    user.Email,

                Name =
                    user.Name,

                PictureUrl =
                    user.PictureUrl
            }
        };

        return Ok(response);
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
    var userId = User.FindFirst(
        System.Security.Claims.ClaimTypes.NameIdentifier
    )?.Value;

    var name = User.FindFirst(
        System.Security.Claims.ClaimTypes.Name
    )?.Value;

    var email = User.Claims
        .FirstOrDefault(c =>
            c.Type == System.Security.Claims.ClaimTypes.Email ||
            c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email
        )?.Value;

    return Ok(new
    {
        userId,
        name,
        email
    });
}


}