using DocIntegrity.Api.Models;

namespace DocIntegrity.Api.Services.Interfaces;

public interface IJwtService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}