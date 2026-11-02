/*
 * Comparación de versiones (POST /api/comparisons y, con un documento de la
 * biblioteca, POST /api/comparisons/documents/{id}). El veredicto lo decide el
 * backend con las huellas SHA-256 y un diff determinista del texto extraído;
 * aquí solo se presenta. Nada de este módulo interpreta texto con IA.
 */
import { normalizeBlockchainStatus } from './adapters.js';

/*
 * Estado informativo del registro en la blockchain activa. Es una pregunta distinta
 * de si el original coincide con la huella guardada en PostgreSQL: una cosa no implica la otra.
 */
export const REGISTERED_CHAIN = {
  confirmed: { tone: 'seal', icon: 'check', label: 'Confirmado en la blockchain activa' },
  not_registered: { tone: 'warn', icon: 'alert', label: 'Sin registro en la blockchain activa' },
  unconfirmed: { tone: 'warn', icon: 'alert', label: 'Huella en el contrato, transacción sin confirmar' },
  unavailable: { tone: 'neutral', icon: 'info', label: 'Blockchain no disponible' },
};

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

/* Documento de la biblioteca con el que se comparó (null en la comparación libre). */
function normalizeRegistered(d) {
  if (!d) return null;
  const b = d.blockchain || {};
  return {
    ...d,
    sha256: String(d.sha256 || '').toLowerCase(),
    originalMatchesRegisteredSha256: !!d.originalMatchesRegisteredSha256,
    blockchain: {
      // Sin un estado conocido no se presume nada: se trata como no disponible.
      status: REGISTERED_CHAIN[b.status] ? b.status : 'unavailable',
      message: typeof b.message === 'string' ? b.message : '',
      failureCode: b.failureCode || null,
      detail: b.detail ? normalizeBlockchainStatus(b.detail) : null,
      checkedAt: b.checkedAt || null,
    },
  };
}

/* Normaliza la respuesta: listas siempre presentes y huellas en minúsculas. */
export function normalizeComparison(r) {
  const file = (f = {}) => ({ ...f, sha256: String(f.sha256 || '').toLowerCase() });
  return {
    ...r,
    registeredDocument: normalizeRegistered(r.registeredDocument),
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
  if (code === 'document_not_found') return 'El documento registrado no existe en tu biblioteca.';
  if (code === 'original_hash_mismatch') return 'El archivo original no es el documento registrado: su SHA-256 no coincide con la huella guardada. No se hizo la comparación.';
  if (e?.status === 503 && code === 'comparison_busy') return 'Hay demasiadas comparaciones en curso. Espera unos segundos e inténtalo de nuevo.';
  return e?.message || 'No se pudo comparar los documentos.';
}

/* ---------- Explicación con IA (POST /api/comparisons/{id}/explanation) ---------- */

export const RELEVANCE = {
  alta: { label: 'Relevancia alta', tone: 'warn' },
  media: { label: 'Relevancia media', tone: 'info' },
  baja: { label: 'Relevancia baja', tone: 'neutral' },
};

export function normalizeExplanation(e) {
  return {
    ...e,
    summary: typeof e.summary === 'string' ? e.summary : '',
    changes: Array.isArray(e.changes) ? e.changes : [],
    discardedChangeIds: Array.isArray(e.discardedChangeIds) ? e.discardedChangeIds : [],
    unexplainedChangeIds: Array.isArray(e.unexplainedChangeIds) ? e.unexplainedChangeIds : [],
    groundingWarnings: Array.isArray(e.groundingWarnings) ? e.groundingWarnings : [],
  };
}

/* Explicaciones y avisos indexados por id de cambio. */
export function explanationIndex(explanation) {
  const byId = new Map();
  for (const c of explanation?.changes || []) byId.set(c.id, { ...c, warnings: [] });
  for (const w of explanation?.groundingWarnings || []) if (w.changeId && byId.has(w.changeId)) byId.get(w.changeId).warnings.push(w.message);
  return byId;
}

/* Mensaje de error de la explicación. El resultado determinista nunca se ve afectado. */
export function explanationErrorText(e) {
  const code = e?.data?.code;
  if (code === 'comparison_not_found') return 'La comparación caducó o ya no está disponible (se conserva 15 minutos). Vuelve a comparar los archivos para pedir la explicación.';
  if (code === 'ai_unavailable') return 'Ollama no está disponible. Comprueba que esté en ejecución e inténtalo de nuevo.';
  if (code === 'ai_timeout') return 'Ollama tardó demasiado en redactar la explicación. Inténtalo de nuevo.';
  if (code === 'ai_rejected') return 'La IA devolvió una respuesta con un formato inválido y se descartó. Inténtalo de nuevo.';
  if (code === 'ai_busy') return 'La IA está explicando otra comparación. Inténtalo de nuevo en unos segundos.';
  if (e?.kind === 'timeout') return 'No hubo respuesta a tiempo. Inténtalo de nuevo.';
  if (e?.kind === 'network') return 'No se pudo conectar con la API de DocIntegrity.';
  if (e?.status === 401) return 'Tu sesión no es válida o expiró. Inicia sesión de nuevo.';
  if (e?.status === 403) return 'La solicitud fue rechazada por la protección contra CSRF. Recarga la página e inténtalo de nuevo.';
  return e?.message || 'No se pudo generar la explicación.';
}
