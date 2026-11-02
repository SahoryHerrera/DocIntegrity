namespace DocIntegrity.Api.DTOs;

// Respuesta de POST /api/comparisons/{id}/explanation.
// Es una INTERPRETACIÓN generada por IA de los cambios que ya detectó la comparación
// determinista. No altera el veredicto, los cambios ni su ubicación.
public class ComparisonExplanationDto
{
    public Guid ComparisonId { get; set; }

    public bool GeneratedByAi { get; set; } = true;

    public string Model { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; }

    public string Summary { get; set; } = string.Empty;

    // Solo cambios cuyo id existe en la comparación.
    public List<ChangeExplanationDto> Changes { get; set; } = [];

    // Ids que devolvió la IA y no existen en la comparación: se descartaron.
    public List<string> DiscardedChangeIds { get; set; } = [];

    // Cambios enviados a la IA que no recibieron explicación.
    public List<string> UnexplainedChangeIds { get; set; } = [];

    // Afirmaciones que no se pudieron comprobar con los cambios detectados (cifras, correos, contradicciones).
    public List<GroundingWarningDto> GroundingWarnings { get; set; } = [];

    public int ChangesSent { get; set; }

    // Cambios no enviados a la IA por el límite de contexto.
    public int ChangesOmitted { get; set; }

    public string Note { get; set; } =
        "Interpretación generada por IA. Los cambios, su ubicación y el veredicto los determina la comparación determinista; verifica cada punto en la tabla.";
}

public class ChangeExplanationDto
{
    public string Id { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    // alta | media | baja (opinión de la IA, no un hecho)
    public string Relevance { get; set; } = "media";
}

public class GroundingWarningDto
{
    // null: el aviso es sobre el resumen general.
    public string? ChangeId { get; set; }
    public string Message { get; set; } = string.Empty;
}
