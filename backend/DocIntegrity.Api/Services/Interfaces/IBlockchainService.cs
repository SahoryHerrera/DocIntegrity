namespace DocIntegrity.Api.Services.Interfaces;

public interface IBlockchainService
{
    Task<(string TransactionHash, long BlockNumber)> RegisterDocumentAsync(
        string sha256
    );

    Task<bool> IsDocumentRegisteredAsync(
        string sha256
    );
}