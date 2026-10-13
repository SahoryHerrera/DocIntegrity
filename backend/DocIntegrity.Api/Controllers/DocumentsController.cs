using System.Text.Json;
using DocIntegrity.Api.Auth;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Exceptions;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocIntegrity.Api.Controllers;

// Bibliotecas privadas: cada usuario solo ve, consulta y verifica sus propios
// documentos. El propietario sale siempre del JWT validado, nunca de la solicitud.
[ApiController]
[Authorize]
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
    // USUARIO ACTUAL Y PROPIEDAD
    // =========================================================

    // Id del usuario del JWT, comprobando que siga existiendo en la base de datos.
    private async Task<Guid?> GetCurrentUserIdAsync()
    {
        var userId = User.GetUserId();

        if (userId is null)
        {
            return null;
        }

        var exists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId);

        return exists ? userId : null;
    }

    private UnauthorizedObjectResult InvalidSession() => Unauthorized(new
    {
        message = "Tu sesión no es válida. Inicia sesión de nuevo.",
        code = "unauthorized"
    });

    // Mismo 404 para un documento ajeno que para uno inexistente:
    // no se confirma la existencia de documentos de otras personas.
    private NotFoundObjectResult DocumentNotFound() => NotFound(new
    {
        message =
            "El documento registrado no fue encontrado."
    });

    private IQueryable<Document> OwnDocuments(Guid userId) =>
        _dbContext.Documents.Where(document => document.RegisteredByUserId == userId);

    private ConflictObjectResult DuplicateForUser(Guid documentId, string sha256) => Conflict(new
    {
        message = "Ya tienes este documento en tu biblioteca.",
        code = "duplicate_document",
        documentId,
        sha256
    });

    // Evidencia de una huella que ya estaba en la cadena activa: se reutiliza
    // solo si la transacción, el evento y el bloque se confirman de verdad.
    private async Task<(string TransactionHash, long BlockNumber)> RequireConfirmedEvidenceAsync(string sha256)
    {
        var evidence = await _blockchainService.FindConfirmedRegistrationAsync(sha256);

        if (evidence is null)
        {
            _logger.LogWarning("La huella figura en el contrato, pero no se encontró una transacción confirmada que la registre.");

            throw new ExternalServiceException(
                ExternalService.Blockchain,
                ExternalFailure.Rejected,
                "La huella figura en la blockchain, pero no se pudo confirmar la transacción que la registró. El documento no se guardó."
            );
        }

        return evidence.Value;
    }

    private static bool IsOwnerShaUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "UX_Documents_Owner_Sha256"
        };

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
        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            return InvalidSession();
        }

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

        // Las solicitudes con la misma huella se procesan de una en una en este
        // servidor: la segunda ve la evidencia de la primera y no envía otra
        // transacción. Entre varios servidores protege el rechazo del contrato.
        var gate = HashLocks[Convert.ToByte(hash[..2], 16)];

        await gate.WaitAsync(HttpContext.RequestAborted);

        try
        {
            return await RegisterHashedAsync(userId.Value, file!, hash);
        }
        finally
        {
            gate.Release();
        }
    }

    // 256 semáforos fijos, uno por primer byte de la huella: memoria acotada.
    private static readonly SemaphoreSlim[] HashLocks =
        Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private async Task<ActionResult<RegisterDocumentResponseDto>> RegisterHashedAsync(
        Guid userId,
        IFormFile file,
        string hash)
    {
        // =====================================================
        // 2. DUPLICADO EN LA BIBLIOTECA DEL USUARIO (antes de la IA)
        // Solo se mira la biblioteca propia: los documentos de otras
        // personas no influyen en la respuesta.
        // =====================================================

        var ownExisting = await OwnDocuments(userId)
            .Where(document => document.Sha256 == hash)
            .Select(document => document.Id)
            .FirstOrDefaultAsync();

        if (ownExisting != Guid.Empty)
        {
            return DuplicateForUser(ownExisting, hash);
        }

        // =====================================================
        // 3. ¿LA HUELLA YA ESTÁ EN LA BLOCKCHAIN ACTIVA?
        // Si la red no responde, se detiene aquí (503) antes de la IA.
        // Si ya está, la evidencia se confirma ANTES de analizar y
        // no se envía otra transacción.
        // =====================================================

        (string TransactionHash, long BlockNumber)? evidence = null;

        if (await _blockchainService.IsDocumentRegisteredAsync(hash))
        {
            evidence = await RequireConfirmedEvidenceAsync(hash);
        }

        // =====================================================
        // 4. EXTRAER TEXTO DEL PDF
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
        // 5. ANALIZAR DOCUMENTO CON IA (siempre, para cada usuario)
        // =====================================================

        var analysis = await _aiService
            .AnalyzeDocumentAsync(extractedText);

        // =====================================================
        // 6. REGISTRAR SHA-256 EN BLOCKCHAIN (solo si hace falta)
        // =====================================================

        var evidenceReused = evidence is not null;

        if (evidence is null)
        {
            try
            {
                var receipt = await _blockchainService
                    .RegisterDocumentAsync(hash);

                evidence = (receipt.TransactionHash, receipt.BlockNumber);
            }
            catch (DocumentAlreadyOnChainException)
            {
                // Concurrencia: otra solicitud registró la misma huella entre la
                // comprobación y el envío. Solo este rechazo específico del contrato
                // lleva a reutilizar, y solo con la evidencia confirmada.
                evidence = await RequireConfirmedEvidenceAsync(hash);
                evidenceReused = true;
            }
        }

        // =====================================================
        // 7. GUARDAR EN POSTGRESQL, A NOMBRE DEL USUARIO
        // =====================================================

        var document = new Document
        {
            Id = Guid.NewGuid(),

            FileName = file.FileName,

            Size = file.Length,

            Sha256 = hash,

            RegisteredAt = DateTime.UtcNow,

            RegisteredByUserId = userId,

            BlockchainEvidenceReused = evidenceReused,

            BlockchainTransactionHash =
                evidence.Value.TransactionHash,

            BlockchainBlockNumber =
                evidence.Value.BlockNumber,

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

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsOwnerShaUniqueViolation(ex))
        {
            // Dos solicitudes simultáneas del mismo usuario con el mismo archivo:
            // el índice único deja pasar solo una.
            _dbContext.ChangeTracker.Clear();

            var existingId = await OwnDocuments(userId)
                .Where(existing => existing.Sha256 == hash)
                .Select(existing => existing.Id)
                .FirstAsync();

            return DuplicateForUser(existingId, hash);
        }

        // =====================================================
        // 8. RESPUESTA COMPLETA
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
                    document.BlockchainBlockNumber,

                EvidenceReused =
                    document.BlockchainEvidenceReused
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
    public async Task<ActionResult<IEnumerable<DocumentDto>>> GetDocuments()
    {
        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            return InvalidSession();
        }

        // Solo la biblioteca del usuario. Los históricos sin propietario no aparecen.
        var documents = await OwnDocuments(userId.Value)
            .AsNoTracking()
            .OrderByDescending(
                document => document.RegisteredAt
            )
            .ToListAsync();

        return Ok(documents.Select(DocumentDto.From));
    }

    // =========================================================
    // OBTENER DOCUMENTO POR ID
    // =========================================================

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetDocumentById(
        Guid id)
    {
        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            return InvalidSession();
        }

        var document = await OwnDocuments(userId.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                document => document.Id == id
            );

        if (document is null)
        {
            return DocumentNotFound();
        }

        return Ok(DocumentDto.From(document));
    }

    // =========================================================
    // ESTADO EN LA BLOCKCHAIN ACTIVA (SOLO LECTURA, SIN GAS)
    // =========================================================

    [HttpGet("{id:guid}/blockchain-status")]
    public async Task<ActionResult<BlockchainStatusDto>> GetBlockchainStatus(
        Guid id)
    {
        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            return InvalidSession();
        }

        var document = await OwnDocuments(userId.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                document => document.Id == id
            );

        if (document is null)
        {
            return DocumentNotFound();
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
        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            return InvalidSession();
        }

        var fileError = ValidateFile(file);

        if (fileError is not null)
        {
            return BadRequest(new
            {
                message = fileError
            });
        }

        // Solo se verifica contra registros propios; uno ajeno da el mismo 404.
        var registeredDocument = await OwnDocuments(userId.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                document => document.Id == id
            );

        if (registeredDocument is null)
        {
            return DocumentNotFound();
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