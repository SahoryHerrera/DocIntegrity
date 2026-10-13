namespace DocIntegrity.Api.DTOs;

public class GoogleLoginRequestDto
{
    public string IdToken { get; set; } = string.Empty;

    // Solo para clientes que no son navegadores (scripts, herramientas):
    // si es true, el JWT se devuelve en la respuesta para usarlo con
    // Authorization: Bearer. React no lo usa: recibe la cookie HttpOnly.
    public bool ReturnToken { get; set; }
}
