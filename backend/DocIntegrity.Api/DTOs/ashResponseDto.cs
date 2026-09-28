namespace DocIntegrity.Api.DTOs;

public class HashResponseDto
{
    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}