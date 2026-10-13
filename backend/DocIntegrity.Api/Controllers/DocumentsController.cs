using System.Text.Json;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Exceptions;
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
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IHashService hashService,
        IBlockchainService blockchainService,
        IDocumentTextExtractorService textExtractorService,
        IAiService aiService,
        AppDbContext dbContext,
        ILogger<DocumentsController> logger)
    {
        _hashService = hashService;
        _blockchainService = blockchainService;
        _textExtractorService = textExtractorService;
        _aiService = aiService;
        _dbContext = dbContext;
        _logger = logger;
    }

    // =========================================================
    // VALIDACIÓN DE ARCHIVOS
    // El parámetro es IFormFile? para responder nosotros con
    // { message } en lugar del error de validación automático.
    // =========================================================

    private static string? ValidateFile(IFormFile? file)
    {
        if (file is null)
        {
            return "Debes enviar un archivo en el campo \"file\".";
        }

        if (file.Length == 0)
        {
            return "El archivo está vacío.";
        }

        return null;
    }

    private static async Task<string?> ValidatePdfAsync(IFormFile? file)
    {
        var error = ValidateFile(file);

        if (error is not null)
        {
            return error;
        }

        if (!string.Equals(
                Path.GetExtension(file!.FileName),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Actualmente solo se admiten archivos PDF.";
        }

        // Un PDF válido contiene la firma %PDF- al inicio del archivo.
        var header = new byte[1024];
        int read;

        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        }

        var signature = "%PDF-"u8;

        if (header.AsSpan(0, read).IndexOf(signature) < 0)
        {
            return "El archivo no es un PDF válido.";
        }

        return null;
    }

    private async Task<string?> TryExtractTextAsync(IFormFile file)
    {
        try
        {
            await using var stream = file.OpenReadStream();

            return await _textExtractorService.ExtractTextAsync(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo leer el PDF {FileName}", file.FileName);

            return null;
        }
    }

    // =========================================================
    // REGISTRAR DOCUMENTO
    // SHA-256 + IA + BLOCKCHAIN + POSTGRESQL
    // =========================================================

    [HttpPost("register")]
    public async Task<ActionResult<RegisterDocumentResponseDto>> RegisterDocument(
        [FromForm] IFormFile? file)
    {
        var fileError = await ValidatePdfAsync(file);

        if (fileError is not null)
        {
            return BadRequest(new
            {
                message = fileError
            });
        }

        // =====================================================
        // 1. CALCULAR SHA-256
        // =====================================================

        string hash;

        await using (var hashStream = file!.OpenReadStream())
        {
            hash = await _hashService
                .CalculateSha256Async(hashStream);
        }

        // =====================================================
        // 2. RECHAZAR DUPLICADOS ANTES DE USAR LA IA
        // PostgreSQL y la blockchain activa se consultan por
        // separado: pueden no coincidir si el nodo se reinició.
        // =====================================================

        var existingRecords = await _dbContext.Documents
            .Where(document => document.Sha256 == hash)
            .OrderBy(document => document.RegisteredAt)
            .Select(document => document.Id)
            .ToListAsync();

        if (existingRecords.Count > 0)
        {
            // Si la red no responde, el duplicado en PostgreSQL basta para rechazar.
            bool? registeredInBlockchain;

            try
            {
                registeredInBlockchain = await _blockchainService
                    .IsDocumentRegisteredAsync(hash);
            }
            catch (ExternalServiceException)
            {
                registeredInBlockchain = null;
            }

            return Conflict(new
            {
                message = registeredInBlockchain switch
                {
                    true => "Este documento ya está registrado.",
                    false => "Este documento ya está registrado en PostgreSQL, pero su huella no aparece en la blockchain activa.",
                    null => "Este documento ya está registrado. No se pudo consultar la blockchain para confirmar su estado."
                },
                code = "duplicate_document",
                documentId = existingRecords[0],
                sha256 = hash,
                existsInDatabase = true,
                databaseRecords = existingRecords.Count,
                registeredInBlockchain
            });
        }

        // Si la red no responde, se detiene aquí (503) antes del análisis con IA.
        if (await _blockchainService.IsDocumentRegisteredAsync(hash))
        {
            return Conflict(new
            {
                message =
                    "La huella de este documento ya está registrada en la blockchain activa, pero no existe en PostgreSQL.",
                code = "duplicate_document",
                documentId = (Guid?)null,
                sha256 = hash,
                existsInDatabase = false,
                databaseRecords = 0,
                registeredInBlockchain = (bool?)true
            });
        }

        // =====================================================
        // 3. EXTRAER TEXTO DEL PDF
        // =====================================================

        var extractedText = await TryExtractTextAsync(file);

        if (extractedText is null)
        {
            return BadRequest(new
            {
                message =
                    "No fue posible leer el contenido del PDF. Puede estar dañado o protegido."
            });
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
        // 4. ANALIZAR DOCUMENTO CON IA
        // =====================================================

        var analysis = await _aiService
            .AnalyzeDocumentAsync(extractedText);

        // =====================================================
        // 5. REGISTRAR SHA-256 EN BLOCKCHAIN
        // =====================================================

        var blockchainResult = await _blockchainService
            .RegisterDocumentAsync(hash);

        // =====================================================
        // 6. GUARDAR EN POSTGRESQL
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
        // 7. RESPUESTA COMPLETA
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
    // ESTADO EN LA BLOCKCHAIN ACTIVA (SOLO LECTURA, SIN GAS)
    // =========================================================

    [HttpGet("{id:guid}/blockchain-status")]
    public async Task<ActionResult<BlockchainStatusDto>> GetBlockchainStatus(
        Guid id)
    {
        var document = await _dbContext.Documents
            .AsNoTracking()
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

        var chainId = await _blockchainService.GetChainIdAsync();

        var record = await _blockchainService
            .GetDocumentRecordAsync(document.Sha256);

        var storedTransactionHash =
            string.IsNullOrWhiteSpace(document.BlockchainTransactionHash)
                ? null
                : document.BlockchainTransactionHash;

        long? storedBlockNumber = storedTransactionHash is null
            ? null
            : document.BlockchainBlockNumber;

        var storedTransaction = new StoredTransactionStatusDto();

        if (storedTransactionHash is not null)
        {
            var check = await _blockchainService
                .CheckRegistrationTransactionAsync(
                    storedTransactionHash,
                    document.Sha256);

            storedTransaction = new StoredTransactionStatusDto
            {
                FoundInActiveChain = check.Found,
                Succeeded = check.Found ? check.Succeeded : null,
                ActiveChainBlockNumber = check.BlockNumber,
                SentToContract = check.Found ? check.SentToContract : null,
                EmitsDocumentHash = check.Found ? check.EmitsDocumentHash : null,
                Status =
                    !check.Found ? "not_found"
                    : !check.Succeeded ? "failed"
                    : check.SentToContract &&
                      check.EmitsDocumentHash &&
                      check.BlockNumber == storedBlockNumber
                        ? "confirmed"
                        : "mismatch"
            };
        }

        return Ok(new BlockchainStatusDto
        {
            DocumentId = document.Id,
            Sha256 = document.Sha256,
            NetworkChainId = chainId,
            ContractAddress = _blockchainService.ContractAddress,
            RegisteredInBlockchain = record.Exists,
            OnChainRegisteredAt = record.RegisteredAt,
            OnChainRegisteredBy = record.RegisteredBy,
            StoredTransactionHash = storedTransactionHash,
            StoredBlockNumber = storedBlockNumber,
            StoredTransactionConfirmed =
                storedTransaction.Status == "confirmed",
            StoredTransaction = storedTransaction,
            CheckedAt = DateTime.UtcNow
        });
    }

    // =========================================================
    // VERIFICAR INTEGRIDAD
    // =========================================================

    [HttpPost("{id:guid}/verify")]
    public async Task<ActionResult<VerifyResponseDto>> VerifyDocument(
        Guid id,
        [FromForm] IFormFile? file)
    {
        var fileError = ValidateFile(file);

        if (fileError is not null)
        {
            return BadRequest(new
            {
                message = fileError
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

        await using (var stream = file!.OpenReadStream())
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

            HashMatches =
                hashMatches,

            RegisteredInBlockchain =
                currentHashRegisteredInBlockchain,

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
        [FromForm] IFormFile? file)
    {
        var fileError = ValidateFile(file);

        if (fileError is not null)
        {
            return BadRequest(new
            {
                message = fileError
            });
        }

        var extension = Path.GetExtension(file!.FileName);

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
        [FromForm] IFormFile? file)
    {
        var fileError = ValidateFile(file);

        if (fileError is not null)
        {
            return BadRequest(new
            {
                message = fileError
            });
        }

        var extension = Path.GetExtension(file!.FileName);

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