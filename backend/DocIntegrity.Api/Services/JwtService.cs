using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace DocIntegrity.Api.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        var secret =
            _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "La configuración Jwt:Secret no existe."
            );

        var issuer =
            _configuration["Jwt:Issuer"]
            ?? "DocIntegrity.Api";

        var audience =
            _configuration["Jwt:Audience"]
            ?? "DocIntegrity.Client";

        var expirationMinutes =
            int.TryParse(
                _configuration["Jwt:ExpirationMinutes"],
                out var configuredMinutes
            )
                ? configuredMinutes
                : 120;

        var expiresAt =
            DateTime.UtcNow.AddMinutes(expirationMinutes);

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()
            ),

            new(
                JwtRegisteredClaimNames.Email,
                user.Email
            ),

            new(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()
            ),

            new(
                ClaimTypes.Name,
                user.Name
            )
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(secret)
        );

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString =
            new JwtSecurityTokenHandler()
                .WriteToken(token);

        return (
            tokenString,
            expiresAt
        );
    }
}