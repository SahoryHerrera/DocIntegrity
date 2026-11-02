namespace DocIntegrity.Api.Services.Comparison;

// Explicación con IA de una comparación (etapa 7.3). Sección "ComparisonExplanation".
public class ExplanationOptions
{
    public const string SectionName = "ComparisonExplanation";

    // Vigencia de una comparación en la caché (para pedir su explicación).
    public double CacheMinutes { get; set; } = 15;

    // Comparaciones guardadas a la vez en este servidor; al superarse se descartan las más antiguas.
    public int MaxCachedComparisons { get; set; } = 50;

    // Comparaciones guardadas por usuario: al superarse se descarta la más antigua DE ESE usuario,
    // así nadie puede hacer caducar las comparaciones de otras personas mientras quepa en el total.
    public int MaxCachedComparisonsPerUser { get; set; } = 10;

    // Cambios enviados a Ollama y longitud de cada fragmento: acotan el contexto del modelo.
    public int MaxChangesForAi { get; set; } = 40;
    public int MaxFieldChars { get; set; } = 300;
    public int MaxContextChars { get; set; } = 120;

    // Parámetros del modelo: contexto fijo, temperatura baja y semilla fija para respuestas consistentes.
    public int NumCtx { get; set; } = 8192;
    public double Temperature { get; set; } = 0.1;
    public int Seed { get; set; } = 7;
    public int NumPredict { get; set; } = 1500;

    public double TimeoutSeconds { get; set; } = 120;

    // Explicaciones generándose a la vez (Ollama local atiende pocas peticiones en paralelo).
    public int MaxConcurrentExplanations { get; set; } = 1;
    public double QueueTimeoutSeconds { get; set; } = 3;

    // Longitud máxima que se acepta de la IA.
    public int MaxSummaryChars { get; set; } = 1500;
    public int MaxExplanationChars { get; set; } = 600;
}
