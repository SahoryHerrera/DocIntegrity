using DocIntegrity.Api.Auth;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Comparison;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Controllers;

// Comparación determinista de dos versiones de un PDF.
// No guarda los archivos ni su texto, no escribe en PostgreSQL y no envía transacciones.
// La comparación libre no usa la blockchain; la comparación con un documento de la
// biblioteca solo la LEE, como dato informativo que no cambia el veredicto.
// Requiere sesión; la protección CSRF de las solicitudes con cookie la aplica
// CsrfProtectionMiddleware como en el resto de la API.
[ApiController]
[Authorize]
[Route("api/comparisons")]
public class ComparisonsController : ControllerBase
{
    // Dos PDF de hasta 20 MB más el margen del formulario multipart.
    public const long MaxRequestBytes = 41L * 1024 * 1024;

    private static readonly (string Field, string Label)[] Fields =
    [
        ("original", "original"),
        ("modified", "nueva versión")
    ];

    private readonly IDocumentComparisonService _comparisonService;
    private readonly ComparisonGate _gate;
    private readonly ComparisonOptions _options;
    private readonly ComparisonCache _cache;
    private readonly IComparisonExplainer _explainer;
    private readonly ExplanationGate _explanationGate;
    private readonly ExplanationOptions _explanationOptions;
    private readonly IHashService _hashService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ComparisonsController> _logger;

    public ComparisonsController(
        IDocumentComparisonService comparisonService,
        ComparisonGate gate,
        IOptions<ComparisonOptions> options,
        ComparisonCache cache,
        IComparisonExplainer explainer,
        ExplanationGate explanationGate,
        IOptions<ExplanationOptions> explanationOptions,
        IHashService hashService,
        AppDbContext dbContext,
        ILogger<ComparisonsController> logger)
    {
        _comparisonService = comparisonService;
        _gate = gate;
        _options = options.Value;
        _cache = cache;
        _explainer = explainer;
        _explanationGate = explanationGate;
        _explanationOptions = explanationOptions.Value;
        _hashService = hashService;
        _dbContext = dbContext;
        _logger = logger;
    }

    // Id del usuario del JWT, comprobando que siga existiendo (solo lectura).
    private async Task<Guid?> CurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        return userId is not null &&
               await _dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken)
            ? userId
            : null;
    }

    private UnauthorizedObjectResult InvalidSession() => Unauthorized(new
    {
        message = "Tu sesión no es válida. Inicia sesión de nuevo.",
        code = "unauthorized"
    });

    private BadRequestObjectResult MultipartRequired() => BadRequest(new
    {
        message = "Envía los dos archivos como multipart/form-data en los campos \"original\" y \"modified\".",
        code = "multipart_required"
    });

    private ObjectResult ComparisonBusy()
    {
        Response.Headers.RetryAfter = "10";

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            message = "Hay demasiadas comparaciones en curso. Inténtalo de nuevo en unos segundos.",
            code = "comparison_busy"
        });
    }

    // POST /api/comparisons (multipart/form-data: original, modified)
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    // Mayor que el límite de la petición: así lo que se rechaza es la petición (413), no el formulario.
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes * 2)]
    public async Task<ActionResult<ComparisonResultDto>> Compare()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var userId = await CurrentUserIdAsync(cancellationToken);

        if (userId is null)
        {
            return InvalidSession();
        }

        if (!Request.HasFormContentType)
        {
            return MultipartRequired();
        }

        // El turno se toma antes de leer los archivos: memoria y CPU acotadas.
        using var lease = await _gate.TryEnterAsync(TimeSpan.FromSeconds(_options.QueueTimeoutSeconds), cancellationToken);

        if (lease is null)
        {
            return ComparisonBusy();
        }

        var (inputs, error) = await ReadInputsAsync(cancellationToken);

        if (error is not null)
        {
            return error;
        }

        var result = await _comparisonService.CompareAsync(inputs![0], inputs[1], lease, cancellationToken);

        return Ok(CacheIfExplainable(userId.Value, result));
    }

    // POST /api/comparisons/documents/{documentId} (multipart/form-data: original, modified)
    // Compara una nueva versión con un documento de la biblioteca del usuario:
    //   1. El documento debe ser del usuario (ajeno, inexistente o histórico sin propietario: el mismo 404).
    //   2. El SHA-256 del original enviado debe ser la huella guardada en PostgreSQL; si no,
    //      422 original_hash_mismatch sin extraer texto, consultar la blockchain ni guardar nada.
    //   3. Comparación determinista, igual que la libre, y consulta informativa (solo lectura)
    //      del estado en la blockchain activa, que nunca impide la comparación.
    // La nueva versión solo se compara: no se registra en PostgreSQL ni en la blockchain.
    [HttpPost("documents/{documentId:guid}")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes * 2)]
    public async Task<ActionResult<ComparisonResultDto>> CompareWithRegistered(
        Guid documentId,
        [FromServices] BlockchainStatusReader blockchainStatusReader)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var userId = await CurrentUserIdAsync(cancellationToken);

        if (userId is null)
        {
            return InvalidSession();
        }

        // Antes de leer los archivos o pedir turno: solo los documentos propios.
        var document = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.RegisteredByUserId == userId, cancellationToken);

        if (document is null)
        {
            return NotFound(new
            {
                message = "El documento registrado no fue encontrado.",
                code = "document_not_found"
            });
        }

        if (!Request.HasFormContentType)
        {
            return MultipartRequired();
        }

        using var lease = await _gate.TryEnterAsync(TimeSpan.FromSeconds(_options.QueueTimeoutSeconds), cancellationToken);

        if (lease is null)
        {
            return ComparisonBusy();
        }

        var (inputs, error) = await ReadInputsAsync(cancellationToken);

        if (error is not null)
        {
            return error;
        }

        // La huella del original decide si se compara: el backend es la autoridad.
        var original = inputs![0];
        var originalSha256 = await _hashService.CalculateSha256Async(new MemoryStream(original.Content, writable: false));

        if (!string.Equals(originalSha256, document.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new
            {
                message = "El archivo original no es el documento registrado: su SHA-256 no coincide con la huella guardada. No se hizo la comparación.",
                code = "original_hash_mismatch",
                field = "original",
                registeredSha256 = document.Sha256,
                providedSha256 = originalSha256
            });
        }

        // La consulta a la cadena corre a la vez que la comparación y tiene su propio tiempo máximo.
        var blockchainCheck = RegisteredBlockchainCheck.RunAsync(
            blockchainStatusReader,
            document,
            TimeSpan.FromSeconds(_options.BlockchainCheckTimeoutSeconds),
            _logger,
            cancellationToken);

        var result = await _comparisonService.CompareAsync(
            original with { Sha256 = originalSha256 },
            inputs[1],
            lease,
            cancellationToken);

        result.RegisteredDocument = Describe(document, await blockchainCheck);

        return Ok(CacheIfExplainable(userId.Value, result));
    }

    private static RegisteredComparisonDocumentDto Describe(Document document, RegisteredBlockchainCheckDto blockchain) => new()
    {
        Id = document.Id,
        FileName = document.FileName,
        Title = document.AiTitle,
        Size = document.Size,
        Sha256 = document.Sha256,
        RegisteredAt = document.RegisteredAt,
        OriginalMatchesRegisteredSha256 = true,
        Blockchain = blockchain
    };

    // Lee y valida los campos original y modified. Devuelve las entradas o el error que se responde.
    private async Task<(ComparisonInput[]? Inputs, ActionResult? Error)> ReadInputsAsync(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var inputs = new ComparisonInput[Fields.Length];

        for (var i = 0; i < Fields.Length; i++)
        {
            var (field, label) = Fields[i];
            var file = form.Files.GetFile(field);

            var error = PdfFileValidator.ValidateFile(file, field) ?? PdfFileValidator.ValidatePdfExtension(file!);

            if (error is not null)
            {
                return (null, BadRequest(new { message = $"Archivo {label}: {error.Message}", code = error.Code, field }));
            }

            if (file!.Length > _options.MaxFileBytes)
            {
                return (null, StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    message = $"El archivo {label} supera el máximo de {_options.MaxFileBytes / (1024 * 1024)} MB.",
                    code = "file_too_large",
                    field
                }));
            }

            var content = new byte[file.Length];

            await using (var stream = file.OpenReadStream())
            {
                await stream.ReadExactlyAsync(content, cancellationToken);
            }

            if (!PdfFileValidator.HasPdfSignature(content))
            {
                return (null, BadRequest(new { message = $"Archivo {label}: el archivo no es un PDF válido.", code = "invalid_pdf", field }));
            }

            inputs[i] = new ComparisonInput(file.FileName, content);
        }

        return (inputs, null);
    }

    // Solo los resultados con cambios detallados se pueden explicar: se guardan en memoria para su propietario.
    private ComparisonResultDto CacheIfExplainable(Guid userId, ComparisonResultDto result)
    {
        if (result.Verdict == DocumentComparisonService.Verdicts.Modified && result.Changes.Count > 0 && !result.DiffIncomplete)
        {
            var entry = _cache.Add(userId, result);
            result.ComparisonId = entry.Id;
            result.ExpiresAt = entry.ExpiresAt.UtcDateTime;
            result.ExplanationAvailable = true;
        }

        return result;
    }

    // POST /api/comparisons/{id}/explanation
    // Explicación con IA (Ollama) de los cambios de una comparación propia y vigente.
    // No modifica la comparación: si Ollama falla, el resultado determinista sigue intacto.
    [HttpPost("{id:guid}/explanation")]
    public async Task<ActionResult<ComparisonExplanationDto>> Explain(Guid id)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var userId = await CurrentUserIdAsync(cancellationToken);

        if (userId is null)
        {
            return InvalidSession();
        }

        // Mismo 404 si no existe, si venció o si es de otra persona.
        var entry = _cache.Get(id, userId.Value);

        if (entry is null)
        {
            return NotFound(new
            {
                message = $"La comparación no existe o caducó (se conserva {_explanationOptions.CacheMinutes:0} minutos). Vuelve a comparar los archivos.",
                code = "comparison_not_found"
            });
        }

        if (entry.Explanation is not null)
        {
            return Ok(entry.Explanation);
        }

        await entry.Lock.WaitAsync(cancellationToken);

        try
        {
            // Otra solicitud pudo generarla mientras se esperaba.
            if (entry.Explanation is not null)
            {
                return Ok(entry.Explanation);
            }

            using var turn = await _explanationGate.TryEnterAsync(TimeSpan.FromSeconds(_explanationOptions.QueueTimeoutSeconds), cancellationToken);

            if (turn is null)
            {
                Response.Headers.RetryAfter = "15";

                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "La IA está explicando otra comparación. Inténtalo de nuevo en unos segundos.",
                    code = "ai_busy"
                });
            }

            // Los errores de Ollama llegan como ExternalServiceException: ai_unavailable, ai_timeout o ai_rejected.
            entry.Explanation = await _explainer.ExplainAsync(entry.Id, entry.Result, cancellationToken);
            return Ok(entry.Explanation);
        }
        finally
        {
            entry.Lock.Release();
        }
    }
}
