namespace DocIntegrity.Api.DTOs;

public class VerifyResponseDto
{
    public Guid DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string RegisteredSha256 { get; set; } = string.Empty;

    public string CurrentSha256 { get; set; } = string.Empty;

    public bool IsValid { get; set; }

    public string Message { get; set; } = string.Empty;
}