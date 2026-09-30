using System.Text.Json;
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
    private readonly IDocumentTextExtractorService _textExtractorService;
    private readonly IAiService _aiService;
    private readonly AppDbContext _dbContext;

    public DocumentsController(
        IHashService hashService,
        IBlockchainService blockchainService,
        IDocumentTextExtractorService textExtractorService,
        IAiService aiService,
        AppDbContext dbContext)
    {
        _hashService = hashService;
        _blockchainService = blockchainService;
        _textExtractorService = textExtractorService;
        _aiService = aiService;
        _dbContext = dbContext;
    }

    // =========================================================
    // REGISTRAR DOCUMENTO
    // SHA-256 + IA + BLOCKCHAIN + POSTGRESQL
    // =========================================================

    [HttpPost("register")]
    public async Task<ActionResult<RegisterDocumentResponseDto>> RegisterDocument(
        [FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new
            {
                message = "El archivo está vacío."
            });
        }

        var extension = Path.GetExtension(file.FileName);

        if (!string.Equals(
                extension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message =
                    "Actualmente el registro inteligente admite archivos PDF."
            });
        }

        // =====================================================
        // 1. CALCULAR SHA-256
        // =====================================================

        string hash;

        await using (var hashStream = file.OpenReadStream())
        {
            hash = await _hashService
                .CalculateSha256Async(hashStream);
        }

        // =====================================================
        // 2. EXTRAER TEXTO DEL PDF
        // =====================================================

        string extractedText;

        await using (var textStream = file.OpenReadStream())
        {
            extractedText = await _textExtractorService
                .ExtractTextAsync(textStream);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return BadRequest(new
            {
                message =
                    "No fue posible extraer texto del documento."
            });
        }

        // =====================================================
        // 3. ANALIZAR DOCUMENTO CON IA
        // =====================================================

        var analysis = await _aiService
            .AnalyzeDocumentAsync(extractedText);

        // =====================================================
        // 4. REGISTRAR SHA-256 EN BLOCKCHAIN
        // =====================================================

        var blockchainResult = await _blockchainService
            .RegisterDocumentAsync(hash);

        // =====================================================
        // 5. GUARDAR EN POSTGRESQL
        // =====================================================

        var document = new Document
        {
            Id = Guid.NewGuid(),

            FileName = file.FileName,

            Size = file.Length,

            Sha256 = hash,

            RegisteredAt = DateTime.UtcNow,

            BlockchainTransactionHash =
                blockchainResult.TransactionHash,

            BlockchainBlockNumber =
                blockchainResult.BlockNumber,

            AiDocumentType =
                analysis.DocumentType,

            AiTitle =
                analysis.Title,

            AiSummary =
                analysis.Summary,

            AiMetadataJson =
                JsonSerializer.Serialize(
                    analysis.Metadata
                )
        };

        _dbContext.Documents.Add(document);

        await _dbContext.SaveChangesAsync();

        // =====================================================
        // 6. RESPUESTA COMPLETA
        // =====================================================

        var response = new RegisterDocumentResponseDto
        {
            Id = document.Id,

            FileName = document.FileName,

            Size = document.Size,

            Sha256 = document.Sha256,

            RegisteredAt = document.RegisteredAt,

            Blockchain = new BlockchainRegistrationDto
            {
                TransactionHash =
                    document.BlockchainTransactionHash,

                BlockNumber =
                    document.BlockchainBlockNumber
            },

            Analysis = new DocumentAnalysisDto
            {
                DocumentType =
                    document.AiDocumentType,

                Title =
                    document.AiTitle,

                Summary =
                    document.AiSummary,

                Metadata =
                    analysis.Metadata
            }
        };

        return Ok(response);
    }

    // =========================================================
    // OBTENER DOCUMENTOS REGISTRADOS
    // =========================================================

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Document>>> GetDocuments()
    {
        var documents = await _dbContext.Documents
            .OrderByDescending(
                document => document.RegisteredAt
            )
            .ToListAsync();

        return Ok(documents);
    }

    // =========================================================
    // OBTENER DOCUMENTO POR ID
    // =========================================================

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Document>> GetDocumentById(
        Guid id)
    {
        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(
                document => document.Id == id
            );

        if (document is null)
        {
            return NotFound(new
            {
                message =
                    "El documento registrado no fue encontrado."
            });
        }

        return Ok(document);
    }

    // =========================================================
    // VERIFICAR INTEGRIDAD
    // =========================================================

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
            .FirstOrDefaultAsync(
                document => document.Id == id
            );

        if (registeredDocument is null)
        {
            return NotFound(new
            {
                message =
                    "El documento registrado no fue encontrado."
            });
        }

        string currentHash;

        await using (var stream = file.OpenReadStream())
        {
            currentHash = await _hashService
                .CalculateSha256Async(stream);
        }

        var hashMatches = string.Equals(
            registeredDocument.Sha256,
            currentHash,
            StringComparison.OrdinalIgnoreCase
        );

        var currentHashRegisteredInBlockchain =
            await _blockchainService
                .IsDocumentRegisteredAsync(currentHash);

        var isValid =
            hashMatches &&
            currentHashRegisteredInBlockchain;

        var response = new VerifyResponseDto
        {
            DocumentId =
                registeredDocument.Id,

            FileName =
                file.FileName,

            RegisteredSha256 =
                registeredDocument.Sha256,

            CurrentSha256 =
                currentHash,

            IsValid =
                isValid,

            Message = isValid
                ? "El documento conserva su integridad y su huella coincide con la registrada en blockchain."
                : !currentHashRegisteredInBlockchain
                    ? "La huella actual del documento no se encuentra registrada en blockchain."
                    : "El documento no corresponde al registro seleccionado."
        };

        return Ok(response);
    }

    // =========================================================
    // EXTRAER TEXTO DE PDF
    // =========================================================

    [HttpPost("extract-text")]
    public async Task<IActionResult> ExtractText(
        [FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new
            {
                message = "El archivo está vacío."
            });
        }

        var extension = Path.GetExtension(file.FileName);

        if (!string.Equals(
                extension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message =
                    "Actualmente solo se admite la extracción de texto desde archivos PDF."
            });
        }

        string extractedText;

        await using (var stream = file.OpenReadStream())
        {
            extractedText = await _textExtractorService
                .ExtractTextAsync(stream);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return BadRequest(new
            {
                message =
                    "No fue posible extraer texto del documento."
            });
        }

        return Ok(new
        {
            fileName = file.FileName,

            text = extractedText
        });
    }

    // =========================================================
    // ANALIZAR DOCUMENTO CON IA
    // =========================================================

    [HttpPost("analyze")]
    public async Task<ActionResult<DocumentAnalysisDto>> AnalyzeDocument(
        [FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new
            {
                message = "El archivo está vacío."
            });
        }

        var extension = Path.GetExtension(file.FileName);

        if (!string.Equals(
                extension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message =
                    "Actualmente el análisis mediante IA admite archivos PDF."
            });
        }

        string extractedText;

        await using (var stream = file.OpenReadStream())
        {
            extractedText = await _textExtractorService
                .ExtractTextAsync(stream);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return BadRequest(new
            {
                message =
                    "No fue posible extraer texto del documento."
            });
        }

        var analysis = await _aiService
            .AnalyzeDocumentAsync(extractedText);

        return Ok(analysis);
    }
}