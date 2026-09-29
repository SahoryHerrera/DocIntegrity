using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocIntegrity.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IHashService _hashService;
    private readonly IBlockchainService _blockchainService;
    private readonly AppDbContext _dbContext;

    public DocumentsController(
        IHashService hashService,
        IBlockchainService blockchainService,
        AppDbContext dbContext)
    {
        _hashService = hashService;
        _blockchainService = blockchainService;
        _dbContext = dbContext;
    }

    [HttpPost("register")]
    public async Task<ActionResult<HashResponseDto>> RegisterDocument(
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

        var blockchainResult =
            await _blockchainService.RegisterDocumentAsync(hash);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = file.FileName,
            Size = file.Length,
            Sha256 = hash,
            RegisteredAt = DateTime.UtcNow,
            BlockchainTransactionHash = blockchainResult.TransactionHash,
            BlockchainBlockNumber = blockchainResult.BlockNumber
        };

        _dbContext.Documents.Add(document);

        await _dbContext.SaveChangesAsync();

        var response = new HashResponseDto
        {
            FileName = document.FileName,
            Size = document.Size,
            Sha256 = document.Sha256
        };

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Document>>> GetDocuments()
    {
        var documents = await _dbContext.Documents
            .OrderByDescending(document => document.RegisteredAt)
            .ToListAsync();

        return Ok(documents);
    }

    [HttpPost("{id:guid}/verify")]
    public async Task<ActionResult<VerifyResponseDto>> VerifyDocument(
        Guid id,
        [FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new
            {
                message = "El archivo está vacío."
            });
        }

        var registeredDocument = await _dbContext.Documents
            .FirstOrDefaultAsync(document => document.Id == id);

        if (registeredDocument is null)
        {
            return NotFound(new
            {
                message = "El documento registrado no fue encontrado."
            });
        }

        await using var stream = file.OpenReadStream();

        var currentHash =
            await _hashService.CalculateSha256Async(stream);

        var hashMatches = string.Equals(
            registeredDocument.Sha256,
            currentHash,
            StringComparison.OrdinalIgnoreCase
        );

        var currentHashRegisteredInBlockchain =
            await _blockchainService.IsDocumentRegisteredAsync(
                currentHash
            );

        var isValid =
            hashMatches &&
            currentHashRegisteredInBlockchain;

        var response = new VerifyResponseDto
        {
            DocumentId = registeredDocument.Id,
            FileName = file.FileName,
            RegisteredSha256 = registeredDocument.Sha256,
            CurrentSha256 = currentHash,
            IsValid = isValid,

            Message = isValid
                ? "El documento conserva su integridad y su huella coincide con la registrada en blockchain."
                : !currentHashRegisteredInBlockchain
                    ? "La huella actual del documento no se encuentra registrada en blockchain."
                    : "El documento no corresponde al registro seleccionado."
        };

        return Ok(response);
    }
}