using DocIntegrity.Api.Auth;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Comparison;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Controllers;

// Comparación determinista de dos versiones de un PDF.
// No guarda los archivos ni su texto, no escribe en PostgreSQL y no usa la blockchain.
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
    private readonly AppDbContext _dbContext;

    public ComparisonsController(
        IDocumentComparisonService comparisonService,
        ComparisonGate gate,
        IOptions<ComparisonOptions> options,
        AppDbContext dbContext)
    {
        _comparisonService = comparisonService;
        _gate = gate;
        _options = options.Value;
        _dbContext = dbContext;
    }

    // POST /api/comparisons (multipart/form-data: original, modified)
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    // Mayor que el límite de la petición: así lo que se rechaza es la petición (413), no el formulario.
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes * 2)]
    public async Task<ActionResult<ComparisonResultDto>> Compare()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var userId = User.GetUserId();

        if (userId is null ||
            !await _dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return Unauthorized(new
            {
                message = "Tu sesión no es válida. Inicia sesión de nuevo.",
                code = "unauthorized"
            });
        }

        if (!Request.HasFormContentType)
        {
            return BadRequest(new
            {
                message = "Envía los dos archivos como multipart/form-data en los campos \"original\" y \"modified\".",
                code = "multipart_required"
            });
        }

        // El turno se toma antes de leer los archivos: memoria y CPU acotadas.
        using var lease = await _gate.TryEnterAsync(TimeSpan.FromSeconds(_options.QueueTimeoutSeconds), cancellationToken);

        if (lease is null)
        {
            Response.Headers.RetryAfter = "10";

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Hay demasiadas comparaciones en curso. Inténtalo de nuevo en unos segundos.",
                code = "comparison_busy"
            });
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var inputs = new ComparisonInput[Fields.Length];

        for (var i = 0; i < Fields.Length; i++)
        {
            var (field, label) = Fields[i];
            var file = form.Files.GetFile(field);

            var error = PdfFileValidator.ValidateFile(file, field) ?? PdfFileValidator.ValidatePdfExtension(file!);

            if (error is not null)
            {
                return BadRequest(new { message = $"Archivo {label}: {error.Message}", code = error.Code, field });
            }

            if (file!.Length > _options.MaxFileBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    message = $"El archivo {label} supera el máximo de {_options.MaxFileBytes / (1024 * 1024)} MB.",
                    code = "file_too_large",
                    field
                });
            }

            var content = new byte[file.Length];

            await using (var stream = file.OpenReadStream())
            {
                await stream.ReadExactlyAsync(content, cancellationToken);
            }

            if (!PdfFileValidator.HasPdfSignature(content))
            {
                return BadRequest(new { message = $"Archivo {label}: el archivo no es un PDF válido.", code = "invalid_pdf", field });
            }

            inputs[i] = new ComparisonInput(file.FileName, content);
        }

        return Ok(await _comparisonService.CompareAsync(inputs[0], inputs[1], lease, cancellationToken));
    }
}
