using DocIntegrity.Api.Models;

namespace DocIntegrity.Api.Services.Interfaces;

public interface IJwtService
{
    // Duración configurada de la sesión (Jwt:ExpirationMinutes).
    int ExpirationMinutes { get; }

    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
