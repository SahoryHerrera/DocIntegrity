using DocIntegrity.Api.Exceptions;
using DocIntegrity.Api.Services.Interfaces;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace DocIntegrity.Api.Services;

public class BlockchainService : IBlockchainService
{
    private readonly IConfiguration _configuration;

    private const string ContractAbi = """
    [
      {
        "inputs": [
          {
            "internalType": "bytes32",
            "name": "documentHash",
            "type": "bytes32"
          }
        ],
        "name": "registerDocument",
        "outputs": [],
        "stateMutability": "nonpayable",
        "type": "function"
      },
      {
        "inputs": [
          {
            "internalType": "bytes32",
            "name": "documentHash",
            "type": "bytes32"
          }
        ],
        "name": "isDocumentRegistered",
        "outputs": [
          {
            "internalType": "bool",
            "name": "",
            "type": "bool"
          }
        ],
        "stateMutability": "view",
        "type": "function"
      }
    ]
    """;

    public BlockchainService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<(string TransactionHash, long BlockNumber)>
        RegisterDocumentAsync(string sha256)
    {
        var rpcUrl =
            _configuration["Blockchain:RpcUrl"];

        var contractAddress =
            _configuration["Blockchain:ContractAddress"];

        var privateKey =
            _configuration["Blockchain:PrivateKey"];

        ValidateConfiguration(
            rpcUrl,
            contractAddress,
            privateKey
        );

        var account = new Account(privateKey);

        var web3 = new Web3(
            account,
            rpcUrl
        );

        var contract = web3.Eth.GetContract(
            ContractAbi,
            contractAddress
        );

        var function =
            contract.GetFunction("registerDocument");

        var hashBytes =
            Convert.FromHexString(sha256);

        TransactionReceipt receipt;

        try
        {
            receipt =
                await function.SendTransactionAndWaitForReceiptAsync(
                    account.Address,
                    new HexBigInteger(300000),
                    null,
                    null,
                    hashBytes
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex.InnerException is TimeoutException)
        {
            throw Translate(ex);
        }

        if (receipt.Status?.Value == 0)
        {
            throw new ExternalServiceException(
                ExternalService.Blockchain,
                ExternalFailure.Rejected,
                "La transacción de registro fue revertida por la red blockchain."
            );
        }

        return (
            receipt.TransactionHash,
            (long)receipt.BlockNumber.Value
        );
    }

    public async Task<bool> IsDocumentRegisteredAsync(
        string sha256)
    {
        var rpcUrl =
            _configuration["Blockchain:RpcUrl"];

        var contractAddress =
            _configuration["Blockchain:ContractAddress"];

        var privateKey =
            _configuration["Blockchain:PrivateKey"];

        ValidateConfiguration(
            rpcUrl,
            contractAddress,
            privateKey
        );

        var account =
            new Account(privateKey);

        var web3 =
            new Web3(account, rpcUrl);

        var contract =
            web3.Eth.GetContract(
                ContractAbi,
                contractAddress
            );

        var function =
            contract.GetFunction(
                "isDocumentRegistered"
            );

        var hashBytes =
            Convert.FromHexString(sha256);

        try
        {
            return await function.CallAsync<bool>(
                hashBytes
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex.InnerException is TimeoutException)
        {
            throw Translate(ex);
        }
    }

    // Traduce los errores de Nethereum a errores seguros para el usuario.
    // El detalle técnico se conserva como InnerException para el log.
    private static Exception Translate(Exception ex)
    {
        var detail = string.Join(
            " | ",
            EnumerateChain(ex).Select(e => e.Message)
        );

        if (detail.Contains(
                "Document already registered",
                StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentAlreadyOnChainException(ex);
        }

        if (EnumerateChain(ex).Any(e =>
                e is RpcClientTimeoutException ||
                e is TimeoutException))
        {
            return new ExternalServiceException(
                ExternalService.Blockchain,
                ExternalFailure.Timeout,
                "La red blockchain tardó demasiado en responder.",
                ex
            );
        }

        if (EnumerateChain(ex).Any(e =>
                e is RpcClientUnknownException ||
                e is HttpRequestException ||
                e is System.Net.Sockets.SocketException))
        {
            return new ExternalServiceException(
                ExternalService.Blockchain,
                ExternalFailure.Unavailable,
                "La red blockchain no está disponible. Comprueba que el nodo de Hardhat esté en ejecución.",
                ex
            );
        }

        return new ExternalServiceException(
            ExternalService.Blockchain,
            ExternalFailure.Rejected,
            "La red blockchain rechazó la operación. Comprueba que el contrato esté desplegado en la dirección configurada.",
            ex
        );
    }

    private static IEnumerable<Exception> EnumerateChain(Exception ex)
    {
        for (Exception? current = ex;
             current is not null;
             current = current.InnerException)
        {
            yield return current;
        }
    }

    private static void ValidateConfiguration(
        string? rpcUrl,
        string? contractAddress,
        string? privateKey)
    {
        if (string.IsNullOrWhiteSpace(rpcUrl) ||
            string.IsNullOrWhiteSpace(contractAddress) ||
            string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException(
                "La configuración de blockchain está incompleta."
            );
        }
    }
}