namespace DocIntegrity.Api.Services.Comparison;

// Límites de la comparación de documentos. Se pueden ajustar en la sección
// "Comparison" de la configuración; estos son los valores aprobados en la etapa 7.
public class ComparisonOptions
{
    public const string SectionName = "Comparison";

    // Tamaño máximo de cada PDF. La petición completa también está limitada
    // en ComparisonsController (dos archivos + margen del multipart).
    public long MaxFileBytes { get; set; } = 20L * 1024 * 1024;

    public int MaxPages { get; set; } = 200;

    // Palabras máximas por documento: protege la memoria y el tiempo del diff.
    public int MaxWordsPerDocument { get; set; } = 400_000;

    // Comparaciones que se procesan a la vez en este servidor.
    public int MaxConcurrentComparisons { get; set; } = 2;

    // Cuánto espera una comparación por un turno antes de responder 503.
    public double QueueTimeoutSeconds { get; set; } = 5;

    // Tiempo máximo para extraer el texto de los dos documentos.
    public double ExtractionTimeoutSeconds { get; set; } = 60;

    // Margen adicional para una página que PdfPig no termina de leer: el extractor
    // solo puede detenerse entre páginas. Pasado este margen se responde igualmente.
    public double ExtractionGraceSeconds { get; set; } = 5;

    // Presupuesto de pasos del algoritmo de Myers en cada nivel (palabras, líneas).
    // Al agotarse se pasa a comparar por líneas y, si tampoco alcanza, se informa.
    public long MaxDiffOperations { get; set; } = 200_000_000;

    // Cambios devueltos en la respuesta y longitud del texto de cada lado.
    public int MaxChangesInResponse { get; set; } = 500;

    public int MaxChangeTextLength { get; set; } = 1_000;

    public int ContextWords { get; set; } = 8;
}
