using System.Security.Cryptography;
using DocIntegrity.Api.Services.Interfaces;

namespace DocIntegrity.Api.Services;

public class HashService : IHashService
{
    public async Task<string> CalculateSha256Async(Stream stream)
    {
        var hashBytes = await SHA256.HashDataAsync(stream);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}