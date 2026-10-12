/*
 * Adaptan las respuestas de DocIntegrity.Api a la forma que usan las vistas.
 * No inventan datos: un campo que el backend no envía queda en null.
 */

const text = (v) => (typeof v === 'string' && v.trim() ? v.trim() : null);

function parseMetadata(raw) {
  if (raw && typeof raw === 'object') return raw;
  if (typeof raw !== 'string' || !raw.trim()) return {};
  try {
    const obj = JSON.parse(raw);
    return obj && typeof obj === 'object' && !Array.isArray(obj) ? obj : {};
  } catch {
    return {};
  }
}

/* Solo hay análisis si el backend devolvió al menos el tipo, el título o el resumen. */
function analysisOf(documentType, title, summary, metadata) {
  const ai = { documentType: text(documentType), title: text(title), summary: text(summary), metadata: parseMetadata(metadata) };
  return ai.documentType || ai.title || ai.summary ? ai : null;
}

/* Transacción y bloque: el modelo guarda '' y 0 cuando no hay datos. */
function chainOf(transactionHash, blockNumber) {
  const tx = text(transactionHash);
  return { transactionHash: tx, blockNumber: tx && Number.isFinite(Number(blockNumber)) ? Number(blockNumber) : null };
}

/* Entidad Document de GET /api/documents y GET /api/documents/{id}. */
export function normalizeDocument(d) {
  const chain = chainOf(d.blockchainTransactionHash, d.blockchainBlockNumber);
  return {
    id: d.id,
    fileName: d.fileName,
    size: Number(d.size) || 0,
    sha256: String(d.sha256 || '').toLowerCase(),
    registeredAt: d.registeredAt,
    blockchainTransactionHash: chain.transactionHash,
    blockchainBlockNumber: chain.blockNumber,
    ai: analysisOf(d.aiDocumentType, d.aiTitle, d.aiSummary, d.aiMetadataJson),
  };
}

/* RegisterDocumentResponseDto de POST /api/documents/register. */
export function normalizeRegistration(r) {
  const b = r.blockchain || {};
  const a = r.analysis || {};
  const chain = chainOf(b.transactionHash, b.blockNumber);
  return {
    id: r.id,
    fileName: r.fileName,
    size: Number(r.size) || 0,
    sha256: String(r.sha256 || '').toLowerCase(),
    registeredAt: r.registeredAt,
    blockchainTransactionHash: chain.transactionHash,
    blockchainBlockNumber: chain.blockNumber,
    ai: analysisOf(a.documentType, a.title, a.summary, a.metadata),
  };
}

/*
 * VerifyResponseDto de POST /api/documents/{id}/verify.
 * hashMatches y registeredInBlockchain llegarán del backend con el cambio B1;
 * mientras tanto se deducen solo cuando es lógicamente seguro
 * (isValid = coincide && en cadena). Nunca se interpreta el texto de `message`.
 */
export function normalizeVerification(v) {
  const registered = String(v.registeredSha256 || '').toLowerCase();
  const current = String(v.currentSha256 || '').toLowerCase();
  const hashMatches = typeof v.hashMatches === 'boolean' ? v.hashMatches : registered === current;
  let registeredInBlockchain = typeof v.registeredInBlockchain === 'boolean' ? v.registeredInBlockchain : null;
  if (registeredInBlockchain === null) {
    if (v.isValid) registeredInBlockchain = true;
    else if (hashMatches) registeredInBlockchain = false;
  }
  return {
    documentId: v.documentId,
    fileName: v.fileName,
    registeredSha256: registered,
    currentSha256: current,
    hashMatches,
    registeredInBlockchain, // null = el backend aún no lo informa
    isValid: !!v.isValid,
    message: text(v.message),
  };
}

/* AuthResponseDto de POST /api/auth/google. */
export function normalizeAuth(r) {
  const u = r.user || {};
  return {
    token: r.token,
    expiresAt: r.expiresAt,
    user: { id: u.id, email: u.email, name: u.name, pictureUrl: text(u.pictureUrl) },
  };
}
