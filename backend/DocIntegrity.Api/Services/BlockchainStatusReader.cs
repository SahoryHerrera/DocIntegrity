using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Interfaces;

namespace DocIntegrity.Api.Services;

// Estado de un documento registrado en la blockchain ACTIVA, solo lectura y sin gas.
// Lo usan GET /api/documents/{id}/blockchain-status y la comparación con un documento
// de la biblioteca. Los errores de la red llegan como ExternalServiceException.
public class BlockchainStatusReader
{
    private readonly IBlockchainService _blockchainService;

    public BlockchainStatusReader(IBlockchainService blockchainService)
    {
        _blockchainService = blockchainService;
    }

    public async Task<BlockchainStatusDto> ReadAsync(Document document)
    {
        var chainId = await _blockchainService.GetChainIdAsync();

        var record = await _blockchainService
            .GetDocumentRecordAsync(document.Sha256);

        var storedTransactionHash =
            string.IsNullOrWhiteSpace(document.BlockchainTransactionHash)
                ? null
                : document.BlockchainTransactionHash;

        long? storedBlockNumber = storedTransactionHash is null
            ? null
            : document.BlockchainBlockNumber;

        var storedTransaction = new StoredTransactionStatusDto();

        if (storedTransactionHash is not null)
        {
            var check = await _blockchainService
                .CheckRegistrationTransactionAsync(
                    storedTransactionHash,
                    document.Sha256);

            storedTransaction = new StoredTransactionStatusDto
            {
                FoundInActiveChain = check.Found,
                Succeeded = check.Found ? check.Succeeded : null,
                ActiveChainBlockNumber = check.BlockNumber,
                SentToContract = check.Found ? check.SentToContract : null,
                EmitsDocumentHash = check.Found ? check.EmitsDocumentHash : null,
                Status =
                    !check.Found ? "not_found"
                    : !check.Succeeded ? "failed"
                    : check.SentToContract &&
                      check.EmitsDocumentHash &&
                      check.BlockNumber == storedBlockNumber
                        ? "confirmed"
                        : "mismatch"
            };
        }

        return new BlockchainStatusDto
        {
            DocumentId = document.Id,
            Sha256 = document.Sha256,
            NetworkChainId = chainId,
            ContractAddress = _blockchainService.ContractAddress,
            RegisteredInBlockchain = record.Exists,
            OnChainRegisteredAt = record.RegisteredAt,
            OnChainRegisteredBy = record.RegisteredBy,
            StoredTransactionHash = storedTransactionHash,
            StoredBlockNumber = storedBlockNumber,
            StoredTransactionConfirmed =
                storedTransaction.Status == "confirmed",
            StoredTransaction = storedTransaction,
            CheckedAt = DateTime.UtcNow
        };
    }
}
