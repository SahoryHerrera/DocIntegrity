namespace DocIntegrity.Api.DTOs;

// Resultado de POST /api/comparisons. Todo lo que contiene es determinista:
// huellas, extracción con PdfPig y diff de Myers. La IA no interviene.
public class ComparisonResultDto
{
    // identical | same_text | modified | not_comparable
    public string Verdict { get; set; } = string.Empty;

    // complete | partial | none | not_needed (archivos idénticos: no se extrae texto)
    public string Reliability { get; set; } = string.Empty;

    // Código del motivo cuando Verdict = not_comparable.
    public string? NotComparableReason { get; set; }

    public DateTime ComparedAt { get; set; }

    public ComparisonScopeDto Scope { get; set; } = new();

    public ComparisonFilesDto Files { get; set; } = new();

    public IReadOnlyList<NormalizationStepDto> Normalization { get; set; } = [];

    // word | line (respaldo para documentos muy distintos) | none (sin diff)
    public string Granularity { get; set; } = "none";

    // null cuando no se calculó el diff.
    public ComparisonStatsDto? Stats { get; set; }

    public List<ChangeDto> Changes { get; set; } = [];

    // Cambios detectados en total; Changes puede traer menos (ChangesTruncated).
    public int TotalChanges { get; set; }

    public bool ChangesTruncated { get; set; }

    // true si el diff no pudo completarse (documentos demasiado distintos):
    // el texto difiere, pero el detalle de los cambios no está disponible.
    public bool DiffIncomplete { get; set; }

    public List<MetadataDifferenceDto> MetadataDifferences { get; set; } = [];

    public List<ComparisonWarningDto> Warnings { get; set; } = [];
}

// Qué compara y qué no compara esta herramienta.
public class ComparisonScopeDto
{
    public bool Text { get; set; } = true;
    public bool Images { get; set; }
    public bool Signatures { get; set; }
    public bool Formatting { get; set; }
    public bool Layout { get; set; }
    public bool Annotations { get; set; }
    // Los metadatos del PDF se informan aparte y no influyen en el veredicto.
    public bool MetadataInformative { get; set; } = true;
    public string Note { get; set; } =
        "Se compara el texto extraído. Imágenes, firmas, formato, maquetación y anotaciones no se comparan; same_text no implica que sean iguales.";
}

public class ComparisonFilesDto
{
    public ComparisonFileDto Original { get; set; } = new();
    public ComparisonFileDto Modified { get; set; } = new();
}

public class ComparisonFileDto
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int? PageCount { get; set; }
    public string? PdfVersion { get; set; }
    // null cuando no fue necesario extraer el texto (archivos idénticos).
    public ExtractionReportDto? Extraction { get; set; }
}

public class ExtractionReportDto
{
    // complete | partial | none | encrypted | unreadable | too_many_pages | too_much_text | timeout
    public string Status { get; set; } = string.Empty;
    public bool Encrypted { get; set; }
    public int Words { get; set; }
    public int PagesWithText { get; set; }
    public List<int> BlankPages { get; set; } = [];
    public List<int> SuspectedScannedPages { get; set; } = [];
    public List<int> GarbledPages { get; set; } = [];
    public List<int> FailedPages { get; set; } = [];
}

public class NormalizationStepDto
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

// Unidades = palabras (Granularity = word) o líneas (Granularity = line).
public class ComparisonStatsDto
{
    public int Added { get; set; }
    public int Removed { get; set; }
    public int Replaced { get; set; }
    public int UnitsAdded { get; set; }
    public int UnitsRemoved { get; set; }
    public int UnitsUnchanged { get; set; }
    // 2·iguales / (unidades del original + unidades de la nueva versión).
    public double Similarity { get; set; }
    public List<int> PagesAffectedOriginal { get; set; } = [];
    public List<int> PagesAffectedModified { get; set; } = [];
}

public class ChangeDto
{
    public string Id { get; set; } = string.Empty;
    // added | removed | replaced
    public string Type { get; set; } = string.Empty;
    public ChangeSideDto Original { get; set; } = new();
    public ChangeSideDto Modified { get; set; } = new();
    public ChangeContextDto Context { get; set; } = new();
    public ChangeSectionDto? Section { get; set; }
    // number, money, date, email, invisible, lookalike, mixed_script, case_only, punctuation_only
    public List<string> Tags { get; set; } = [];
    // invisible, lookalike o mixed_script: cambios que pueden pasar desapercibidos a la vista.
    public bool Suspicious { get; set; }
    // El cambio está en una página cuya extracción no es fiable: puede ser un artefacto.
    public bool OnIncompletePage { get; set; }
}

// Página y línea son aproximadas (orden de extracción de PdfPig). En un lado sin
// texto (añadido/eliminado), InsertionPoint indica la posición donde ocurre el cambio.
public class ChangeSideDto
{
    public string Text { get; set; } = string.Empty;
    public bool TextTruncated { get; set; }
    public int? Page { get; set; }
    public int? Line { get; set; }
    public int? EndPage { get; set; }
    public int? EndLine { get; set; }
    public bool InsertionPoint { get; set; }
}

public class ChangeContextDto
{
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

public class ChangeSectionDto
{
    public string Title { get; set; } = string.Empty;
    public int Page { get; set; }
    public bool Approximate { get; set; } = true;
}

public class MetadataDifferenceDto
{
    public string Field { get; set; } = string.Empty;
    public string? Original { get; set; }
    public string? Modified { get; set; }
}

public class ComparisonWarningDto
{
    public string Code { get; set; } = string.Empty;
    // info | warning | blocking
    public string Severity { get; set; } = "warning";
    public string Message { get; set; } = string.Empty;
    // original | modified | null (ambos)
    public string? File { get; set; }
    public List<int>? Pages { get; set; }
}
