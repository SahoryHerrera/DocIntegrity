using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocIntegrity.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IHashService _hashService;

    public DocumentsController(IHashService hashService)
    {
        _hashService = hashService;
    }

    [HttpPost("hash")]
    public async Task<ActionResult<HashResponseDto>> CalculateHash(
        [FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new
            {
                message = "El archivo está vacío."
            });
        }

        await using var stream = file.OpenReadStream();

        var hash = await _hashService.CalculateSha256Async(stream);

        var response = new HashResponseDto
        {
            FileName = file.FileName,
            Size = file.Length,
            Sha256 = hash
        };

        return Ok(response);
    }
}