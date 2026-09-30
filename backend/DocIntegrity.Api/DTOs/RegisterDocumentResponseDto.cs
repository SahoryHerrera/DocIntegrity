namespace DocIntegrity.Api.DTOs;

public class RegisterDocumentResponseDto
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; }

    public BlockchainRegistrationDto Blockchain { get; set; } = new();

    public DocumentAnalysisDto Analysis { get; set; } = new();
}

public class BlockchainRegistrationDto
{
    public string TransactionHash { get; set; } = string.Empty;

    public long BlockNumber { get; set; }
}