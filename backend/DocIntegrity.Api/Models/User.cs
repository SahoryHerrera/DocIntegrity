namespace DocIntegrity.Api.Models;

public class User
{
    public Guid Id { get; set; }

    public string GoogleId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PictureUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime LastLoginAt { get; set; }
}