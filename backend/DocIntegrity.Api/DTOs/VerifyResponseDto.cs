namespace DocIntegrity.Api.DTOs;

public class VerifyResponseDto
{
    public Guid DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string RegisteredSha256 { get; set; } = string.Empty;

    public string CurrentSha256 { get; set; } = string.Empty;

    // La huella del archivo presentado es igual a la registrada en PostgreSQL.
    public bool HashMatches { get; set; }

    // La huella del archivo presentado está registrada en la blockchain activa.
    public bool RegisteredInBlockchain { get; set; }

    public bool IsValid { get; set; }

    public string Message { get; set; } = string.Empty;
}