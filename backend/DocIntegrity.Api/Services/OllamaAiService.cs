using System.Net.Http.Json;
using System.Text.Json;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Services.Interfaces;

namespace DocIntegrity.Api.Services;

public class OllamaAiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OllamaAiService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<DocumentAnalysisDto> AnalyzeDocumentAsync(
        string documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            throw new ArgumentException(
                "El texto del documento no puede estar vacío.",
                nameof(documentText)
            );
        }

        var model =
            _configuration["Ollama:Model"] ?? "gemma3:4b";

        var systemPrompt = """
        Eres un sistema de análisis documental llamado DocIntegrity.

        Tu tarea es analizar documentos y devolver únicamente JSON válido.

        Debes identificar:
        - Tipo de documento.
        - Título principal.
        - Un resumen breve.
        - Metadatos importantes encontrados en el documento.

        No inventes información.

        Si un dato no aparece en el documento, simplemente no lo incluyas
        dentro de metadata.

        Todos los valores dentro de metadata deben ser texto.

        Devuelve exactamente esta estructura:

        {
          "documentType": "tipo del documento",
          "title": "título del documento",
          "summary": "resumen breve",
          "metadata": {
            "clave": "valor"
          }
        }
        """;

        var userPrompt = $"""
        Analiza el siguiente documento:

        ---------------- DOCUMENTO ----------------

        {documentText}

        -------------- FIN DOCUMENTO --------------
        """;

        var request = new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userPrompt
                }
            },
            stream = false,
            format = "json",
            options = new
            {
                temperature = 0.1
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/chat",
            request
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent =
                await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                $"Ollama devolvió el código {(int)response.StatusCode}: {errorContent}"
            );
        }

        var ollamaResponse =
            await response.Content.ReadFromJsonAsync<OllamaChatResponse>();

        if (ollamaResponse?.Message is null ||
            string.IsNullOrWhiteSpace(ollamaResponse.Message.Content))
        {
            throw new InvalidOperationException(
                "Ollama no devolvió un análisis válido."
            );
        }

        return ParseAnalysis(
            ollamaResponse.Message.Content
        );
    }

    private static DocumentAnalysisDto ParseAnalysis(
        string json)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        var result = new DocumentAnalysisDto
        {
            DocumentType = GetStringProperty(
                root,
                "documentType"
            ),

            Title = GetStringProperty(
                root,
                "title"
            ),

            Summary = GetStringProperty(
                root,
                "summary"
            )
        };

        if (root.TryGetProperty(
                "metadata",
                out var metadataElement) &&
            metadataElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in
                     metadataElement.EnumerateObject())
            {
                result.Metadata[property.Name] =
                    property.Value.ValueKind ==
                    JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
            }
        }

        return result;
    }

    private static string GetStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return string.Empty;
        }

        return property.ValueKind ==
               JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }
}