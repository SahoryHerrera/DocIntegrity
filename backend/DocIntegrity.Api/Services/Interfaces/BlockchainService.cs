using DocIntegrity.Api.Services.Interfaces;
using Nethereum.Hex.HexTypes;
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

        var receipt =
            await function.SendTransactionAndWaitForReceiptAsync(
                account.Address,
                new HexBigInteger(300000),
                null,
                null,
                hashBytes
            );

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

        return await function.CallAsync<bool>(
            hashBytes
        );
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