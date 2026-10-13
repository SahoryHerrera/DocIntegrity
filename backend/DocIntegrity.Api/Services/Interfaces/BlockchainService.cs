using System.Numerics;
using DocIntegrity.Api.Exceptions;
using DocIntegrity.Api.Services.Interfaces;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
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
      },
      {
        "inputs": [
          {
            "internalType": "bytes32",
            "name": "documentHash",
            "type": "bytes32"
          }
        ],
        "name": "getDocument",
        "outputs": [
          { "internalType": "bytes32", "name": "", "type": "bytes32" },
          { "internalType": "uint256", "name": "", "type": "uint256" },
          { "internalType": "address", "name": "", "type": "address" },
          { "internalType": "bool", "name": "", "type": "bool" }
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

    public string ContractAddress =>
        _configuration["Blockchain:ContractAddress"] ?? string.Empty;

    // =========================================================
    // CONSULTAS DE SOLO LECTURA
    // No usan la clave privada: no pueden firmar transacciones.
    // =========================================================

    private Web3 CreateReadOnlyWeb3()
    {
        var rpcUrl = _configuration["Blockchain:RpcUrl"];

        if (string.IsNullOrWhiteSpace(rpcUrl) ||
            string.IsNullOrWhiteSpace(ContractAddress))
        {
            throw new InvalidOperationException(
                "La configuración de blockchain está incompleta."
            );
        }

        return new Web3(rpcUrl);
    }

    public async Task<long> GetChainIdAsync()
    {
        var web3 = CreateReadOnlyWeb3();

        try
        {
            var chainId = await web3.Eth.ChainId.SendRequestAsync();

            return (long)chainId.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex.InnerException is TimeoutException)
        {
            throw Translate(ex);
        }
    }

    public async Task<OnChainDocumentRecord> GetDocumentRecordAsync(
        string sha256)
    {
        var web3 = CreateReadOnlyWeb3();

        var function = web3.Eth
            .GetContract(ContractAbi, ContractAddress)
            .GetFunction("getDocument");

        DocumentRecordOutput record;

        try
        {
            record = await function
                .CallDeserializingToObjectAsync<DocumentRecordOutput>(
                    Convert.FromHexString(sha256)
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex.InnerException is TimeoutException)
        {
            throw Translate(ex);
        }

        if (!record.Exists)
        {
            return new OnChainDocumentRecord(false, null, null);
        }

        return new OnChainDocumentRecord(
            true,
            DateTimeOffset
                .FromUnixTimeSeconds((long)record.RegisteredAt)
                .UtcDateTime,
            record.RegisteredBy
        );
    }

    public async Task<StoredTransactionCheck> CheckRegistrationTransactionAsync(
        string transactionHash,
        string sha256)
    {
        var web3 = CreateReadOnlyWeb3();

        TransactionReceipt? receipt;

        try
        {
            receipt = await web3.Eth.Transactions.GetTransactionReceipt
                .SendRequestAsync(transactionHash);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex.InnerException is TimeoutException)
        {
            throw Translate(ex);
        }

        // La red activa no conoce la transacción.
        if (receipt is null)
        {
            return new StoredTransactionCheck(false, false, null, false, false);
        }

        var sentToContract = string.Equals(
            receipt.To,
            ContractAddress,
            StringComparison.OrdinalIgnoreCase
        );

        var expectedHash = Convert.FromHexString(sha256);

        var emitsDocumentHash = receipt
            .DecodeAllEvents<DocumentRegisteredEventDto>()
            .Any(e =>
                string.Equals(
                    e.Log.Address,
                    ContractAddress,
                    StringComparison.OrdinalIgnoreCase) &&
                e.Event.DocumentHash.AsSpan().SequenceEqual(expectedHash));

        return new StoredTransactionCheck(
            true,
            receipt.Status?.Value == 1,
            (long?)receipt.BlockNumber?.Value,
            sentToContract,
            emitsDocumentHash
        );
    }

    [FunctionOutput]
    private sealed class DocumentRecordOutput : IFunctionOutputDTO
    {
        [Parameter("bytes32", "", 1)]
        public byte[] DocumentHash { get; set; } = [];

        [Parameter("uint256", "", 2)]
        public BigInteger RegisteredAt { get; set; }

        [Parameter("address", "", 3)]
        public string RegisteredBy { get; set; } = string.Empty;

        [Parameter("bool", "", 4)]
        public bool Exists { get; set; }
    }

    [Event("DocumentRegistered")]
    private sealed class DocumentRegisteredEventDto : IEventDTO
    {
        [Parameter("bytes32", "documentHash", 1, true)]
        public byte[] DocumentHash { get; set; } = [];

        [Parameter("uint256", "registeredAt", 2, false)]
        public BigInteger RegisteredAt { get; set; }

        [Parameter("address", "registeredBy", 3, true)]
        public string RegisteredBy { get; set; } = string.Empty;
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