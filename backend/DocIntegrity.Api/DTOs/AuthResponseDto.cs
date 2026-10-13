namespace DocIntegrity.Api.DTOs;

public class AuthResponseDto
{
    // Vacío salvo que el cliente pida ReturnToken: los navegadores usan la cookie HttpOnly.
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public UserDto User { get; set; } = new();
}

public class UserDto
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PictureUrl { get; set; } = string.Empty;
}

// Respuesta de GET /api/auth/me.
public class SessionDto
{
    public UserDto User { get; set; } = new();

    public DateTime ExpiresAt { get; set; }

    public int SessionMinutes { get; set; }
}
