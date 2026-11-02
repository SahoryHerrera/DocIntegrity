/*
 * Comparación de versiones (POST /api/comparisons). El veredicto lo decide el
 * backend con las huellas SHA-256 y un diff determinista del texto extraído;
 * aquí solo se presenta. Nada de este módulo interpreta texto con IA.
 */

export const COMPARISON_VERDICTS = {
  identical: {
    tone: 'seal', title: 'Archivos idénticos',
    text: 'Los dos archivos tienen el mismo SHA-256: son el mismo archivo, byte a byte.',
    next: 'No hay ninguna diferencia, ni en el texto ni en el resto del PDF.',
    stamp: 'IDÉNTICOS', ring: 'Mismo SHA-256 · Mismo archivo byte a byte · ',
  },
  same_text: {
    // Tono informativo, nunca el de "íntegro": los archivos NO son el mismo.
    tone: 'info', title: 'Mismo texto, archivos distintos',
    text: 'Las huellas SHA-256 son distintas, pero el texto extraído coincide. Imágenes, firmas, formato y maquetación no se comparan y pueden haber cambiado.',
    next: 'No los trates como el mismo documento. Si necesitas el original exacto, compara la huella.',
    stamp: 'MISMO TEXTO', ring: 'Huellas distintas · Texto extraído igual · ',
  },
  modified: {
    tone: 'alert', title: 'Texto modificado',
    text: 'El texto de la nueva versión difiere del original.',
    next: 'Revisa cada cambio en la tabla antes de aceptar la nueva versión.',
    stamp: 'MODIFICADO', ring: 'El texto cambió · Revisar los cambios · ',
  },
  not_comparable: {
    tone: 'warn', title: 'Comparación no fiable',
    text: 'No se pudo comparar el texto de forma fiable.',
    next: 'Las huellas SHA-256 sí se compararon: consulta el resultado más abajo.',
    stamp: 'NO COMPARABLE', ring: 'Texto no legible · Sin veredicto del texto · ',
  },
};

export const NOT_COMPARABLE_REASONS = {
  encrypted: 'Uno de los PDF está protegido con contraseña: no se puede leer su texto.',
  unreadable: 'Uno de los archivos tiene la firma de un PDF, pero su estructura está dañada o no se puede interpretar.',
  no_text: 'Uno de los PDF no tiene capa de texto (probablemente está escaneado). Compararlo requeriría OCR, que no está disponible.',
  too_many_pages: 'Uno de los PDF supera el máximo de páginas que se pueden comparar.',
  too_much_text: 'Uno de los documentos tiene demasiado texto para compararlo.',
  timeout: 'La extracción del texto tardó más de lo permitido.',
  incomplete_extraction: 'No se encontraron diferencias en el texto legible, pero parte de un documento no se pudo leer: no se puede afirmar que el texto sea el mismo.',
};

export const CHANGE_TYPES = {
  added: { label: 'Añadido', plural: 'Añadidos', tone: 'seal', icon: 'plus' },
  removed: { label: 'Eliminado', plural: 'Eliminados', tone: 'alert', icon: 'x' },
  replaced: { label: 'Reemplazado', plural: 'Reemplazados', tone: 'warn', icon: 'edit' },
};

export const TAGS = {
  number: { label: 'Número', tone: 'neutral' },
  money: { label: 'Importe', tone: 'warn' },
  date: { label: 'Fecha', tone: 'warn' },
  email: { label: 'Correo', tone: 'warn' },
  case_only: { label: 'Solo mayúsculas', tone: 'neutral' },
  punctuation_only: { label: 'Solo puntuación', tone: 'neutral' },
  invisible: { label: 'Caracteres invisibles', tone: 'alert' },
  lookalike: { label: 'Letras parecidas', tone: 'alert' },
  mixed_script: { label: 'Alfabetos mezclados', tone: 'alert' },
};

export const EXTRACTION_STATUS = {
  complete: 'Completa',
  partial: 'Parcial',
  none: 'Sin capa de texto',
  encrypted: 'Protegido con contraseña',
  unreadable: 'Dañado o ilegible',
  too_many_pages: 'Demasiadas páginas',
  too_much_text: 'Demasiado texto',
  timeout: 'Tiempo agotado',
};

export const RELIABILITY = {
  complete: 'Completa: se leyó todo el texto de ambos documentos.',
  partial: 'Parcial: parte de un documento no se pudo leer.',
  none: 'Sin texto comparable.',
  not_needed: 'No hizo falta leer el texto: los archivos son idénticos.',
};

export const METADATA_FIELDS = {
  pageCount: 'Páginas', pdfVersion: 'Versión de PDF', title: 'Título', author: 'Autor', subject: 'Asunto',
  keywords: 'Palabras clave', creator: 'Aplicación de origen', producer: 'Productor del PDF',
  creationDate: 'Fecha de creación', modifiedDate: 'Fecha de modificación',
};

export const FILE_LABEL = { original: 'Original', modified: 'Nueva versión' };

// Gravedad del backend → tono de Huella.
export const WARNING_TONE = { blocking: 'alert', warning: 'warn', info: 'info' };
const SEVERITY_ORDER = { blocking: 0, warning: 1, info: 2 };

export const sortWarnings = (warnings = []) =>
  [...warnings].sort((a, b) => (SEVERITY_ORDER[a.severity] ?? 3) - (SEVERITY_ORDER[b.severity] ?? 3));

// Unidad de las estadísticas: palabras o líneas según la granularidad del diff.
export const unitName = (granularity, n) => (granularity === 'line' ? (n === 1 ? 'línea' : 'líneas') : (n === 1 ? 'palabra' : 'palabras'));

// Páginas en las que está un cambio (en cualquiera de las dos versiones).
export const changePages = (c) => [...new Set([c.original?.page, c.modified?.page].filter((p) => Number.isInteger(p)))];

export function filterChanges(changes, { type = 'all', page = 'all', suspicious = false }) {
  const p = page === 'all' ? null : Number(page);
  return changes.filter((c) =>
    (type === 'all' || c.type === type) &&
    (p === null || changePages(c).includes(p)) &&
    (!suspicious || c.suspicious));
}

export const pagesOf = (changes) => [...new Set(changes.flatMap(changePages))].sort((a, b) => a - b);

export const locationText = (side) => {
  if (!Number.isInteger(side?.page)) return '—';
  const where = `p. ${side.page}${Number.isInteger(side.line) ? `, l. ${side.line}` : ''}`;
  return side.insertionPoint ? `${where} (posición)` : where;
};

/* Normaliza la respuesta: listas siempre presentes y huellas en minúsculas. */
export function normalizeComparison(r) {
  const file = (f = {}) => ({ ...f, sha256: String(f.sha256 || '').toLowerCase() });
  return {
    ...r,
    files: { original: file(r.files?.original), modified: file(r.files?.modified) },
    changes: Array.isArray(r.changes) ? r.changes : [],
    warnings: Array.isArray(r.warnings) ? r.warnings : [],
    metadataDifferences: Array.isArray(r.metadataDifferences) ? r.metadataDifferences : [],
    normalization: Array.isArray(r.normalization) ? r.normalization : [],
  };
}

/* Mensaje de error de la comparación, con el caso de la conexión cerrada por tamaño. */
export function comparisonErrorText(e, totalBytes, maxFileMB) {
  const code = e?.data?.code;
  if (e?.kind === 'network') {
    return totalBytes > maxFileMB * 1024 * 1024
      ? `No se recibió respuesta del servidor. Con archivos grandes, el servidor puede cerrar la conexión antes de responder si la solicitud supera su límite (${maxFileMB} MB por archivo). Si el problema persiste, comprueba que el backend esté en ejecución.`
      : 'No se pudo conectar con la API de DocIntegrity. Comprueba que el backend esté en ejecución.';
  }
  if (e?.kind === 'timeout') return 'El servidor no respondió a tiempo. La extracción de documentos largos puede tardar; inténtalo de nuevo.';
  if (e?.status === 401) return 'Tu sesión no es válida o expiró. Inicia sesión de nuevo.';
  if (e?.status === 403) return code === 'csrf_check_failed'
    ? 'La solicitud fue rechazada por la protección contra CSRF. Recarga la página e inténtalo de nuevo.'
    : 'No tienes permiso para realizar esta acción.';
  if (e?.status === 413) return e.message || `Uno de los archivos supera el máximo de ${maxFileMB} MB.`;
  if (e?.status === 503 && code === 'comparison_busy') return 'Hay demasiadas comparaciones en curso. Espera unos segundos e inténtalo de nuevo.';
  return e?.message || 'No se pudo comparar los documentos.';
}
