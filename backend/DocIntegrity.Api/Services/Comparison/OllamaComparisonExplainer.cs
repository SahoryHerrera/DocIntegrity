using System.Net.Http.Json;
using System.Text.Json;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Exceptions;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Services.Comparison;

public interface IComparisonExplainer
{
    // Lanza ExternalServiceException (Ai) si Ollama no está disponible, tarda demasiado o responde algo inválido.
    Task<ComparisonExplanationDto> ExplainAsync(Guid comparisonId, ComparisonResultDto result, CancellationToken cancellationToken);
}

// Explicación de los cambios deterministas con Ollama (/api/chat).
// Solo recibe los cambios recortados; la respuesta se valida antes de devolverse.
public class OllamaComparisonExplainer : IComparisonExplainer
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ExplanationOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OllamaComparisonExplainer> _logger;

    public OllamaComparisonExplainer(
        HttpClient http,
        IConfiguration configuration,
        IOptions<ExplanationOptions> options,
        TimeProvider time,
        ILogger<OllamaComparisonExplainer> logger)
    {
        _http = http;
        _configuration = configuration;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<ComparisonExplanationDto> ExplainAsync(Guid comparisonId, ComparisonResultDto result, CancellationToken cancellationToken)
    {
        var prompt = ExplanationPrompt.Build(result, _options);
        var model = _configuration["Ollama:Model"] ?? "gemma3:4b";
        var ids = prompt.Changes.Select(c => c.Id).ToArray();

        var request = new
        {
            model,
            stream = false,
            // Salida estructurada: el modelo solo puede usar los ids enviados (el servidor lo vuelve a validar).
            format = new
            {
                type = "object",
                properties = new
                {
                    summary = new { type = "string" },
                    changes = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                id = new { type = "string", @enum = ids },
                                explanation = new { type = "string" },
                                relevance = new { type = "string", @enum = new[] { "alta", "media", "baja" } }
                            },
                            required = new[] { "id", "explanation", "relevance" }
                        }
                    }
                },
                required = new[] { "summary", "changes" }
            },
            options = new
            {
                temperature = _options.Temperature,
                num_ctx = _options.NumCtx,
                seed = _options.Seed,
                num_predict = _options.NumPredict
            },
            messages = new[]
            {
                new { role = "system", content = prompt.SystemPrompt },
                new { role = "user", content = prompt.UserPrompt }
            }
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        string body;
        HttpResponseMessage response;

        try
        {
            response = await _http.PostAsJsonAsync("/api/chat", request, timeout.Token);
            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalServiceException(ExternalService.Ai, ExternalFailure.Timeout,
                $"El modelo de IA (Ollama) no terminó la explicación en {_options.TimeoutSeconds:0} segundos. El resultado de la comparación no cambia; puedes reintentarlo.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceException(ExternalService.Ai, ExternalFailure.Unavailable,
                "El modelo de IA (Ollama) no está disponible. Comprueba que Ollama esté en ejecución. El resultado de la comparación no cambia.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Solo el código: el cuerpo puede repetir texto de los documentos.
                _logger.LogWarning("Ollama respondió {Status} al explicar una comparación", (int)response.StatusCode);

                throw new ExternalServiceException(ExternalService.Ai, ExternalFailure.Rejected,
                    "El modelo de IA (Ollama) no pudo generar la explicación.");
            }
        }

        string content;

        try
        {
            using var document = JsonDocument.Parse(body);
            content = document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ExternalServiceException(ExternalService.Ai, ExternalFailure.Rejected,
                "El modelo de IA (Ollama) devolvió una respuesta con un formato inesperado.", ex);
        }

        ComparisonExplanationDto explanation;

        try
        {
            explanation = ExplanationValidator.Validate(content, prompt, result, _options);
        }
        catch (ExplanationFormatException ex)
        {
            throw new ExternalServiceException(ExternalService.Ai, ExternalFailure.Rejected,
                "La IA devolvió una explicación con un formato inválido. El resultado de la comparación no cambia; puedes reintentarlo.", ex);
        }

        explanation.ComparisonId = comparisonId;
        explanation.Model = model;
        explanation.GeneratedAt = _time.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Explicación generada: {Explained}/{Sent} cambios, {Discarded} ids descartados, {Warnings} avisos",
            explanation.Changes.Count, explanation.ChangesSent, explanation.DiscardedChangeIds.Count, explanation.GroundingWarnings.Count);

        return explanation;
    }
}
