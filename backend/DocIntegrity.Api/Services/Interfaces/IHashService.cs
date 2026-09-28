namespace DocIntegrity.Api.Services.Interfaces;

public interface IHashService
{
    Task<string> CalculateSha256Async(Stream stream);
}