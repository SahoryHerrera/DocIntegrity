/*
 * Funciones de los endpoints reales de DocIntegrity.Api.
 * Cada una corresponde a una acción de AuthController o DocumentsController.
 */
import { API } from '../config.js';
import { normalizeAuth, normalizeBlockchainStatus, normalizeDocument, normalizeRegistration, normalizeSession, normalizeVerification } from './adapters.js';
import { createHttpClient } from './http.js';
import { requestLog } from './requestLog.js';

export { ApiError } from './http.js';

/* Los endpoints reciben el PDF en el campo `file` ([FromForm] IFormFile file). */
function fileForm(file) {
  const form = new FormData();
  form.append('file', file, file.name || 'documento.pdf');
  return form;
}

const docPath = (id) => `/api/documents/${encodeURIComponent(id)}`;

export function createApi(request) {
  return {
    /* GET / → { application, status } */
    health: (opts) => request('GET', '/', { auth: false, ...opts }),

    auth: {
      /* POST /api/auth/google { idToken } → AuthResponseDto; el JWT llega solo en la cookie HttpOnly */
      google: async (idToken, opts) => normalizeAuth(await request('POST', '/api/auth/google', { json: { idToken }, auth: false, ...opts })),
      /* GET /api/auth/me → SessionDto { user, expiresAt, sessionMinutes } */
      me: async (opts) => normalizeSession(await request('GET', '/api/auth/me', opts)),
      /* POST /api/auth/logout → 204, borra la cookie */
      logout: (opts) => request('POST', '/api/auth/logout', { auth: false, ...opts }),
    },

    documents: {
      /* GET /api/documents → Document[] */
      list: async (opts) => (await request('GET', '/api/documents', opts) || []).map(normalizeDocument),
      /* GET /api/documents/{id} → Document */
      get: async (id, opts) => normalizeDocument(await request('GET', docPath(id), opts)),
      /* GET /api/documents/{id}/blockchain-status → BlockchainStatusDto (lectura de la cadena activa, sin gas) */
      blockchainStatus: async (id, opts) => normalizeBlockchainStatus(await request('GET', `${docPath(id)}/blockchain-status`, opts)),
      /* POST /api/documents/register (file) → RegisterDocumentResponseDto */
      register: async (file, opts) => normalizeRegistration(await request('POST', '/api/documents/register', { form: fileForm(file), timeoutMs: API.longTimeoutMs, ...opts })),
      /* POST /api/documents/{id}/verify (file) → VerifyResponseDto */
      verify: async (id, file, opts) => normalizeVerification(await request('POST', `${docPath(id)}/verify`, { form: fileForm(file), timeoutMs: 60_000, ...opts })),
      /* POST /api/documents/analyze (file) → DocumentAnalysisDto, sin guardar */
      analyze: (file, opts) => request('POST', '/api/documents/analyze', { form: fileForm(file), timeoutMs: API.longTimeoutMs, ...opts }),
      /* POST /api/documents/extract-text (file) → { fileName, text }, sin guardar */
      extractText: (file, opts) => request('POST', '/api/documents/extract-text', { form: fileForm(file), ...opts }),
    },
  };
}

const request = createHttpClient({
  baseUrl: API.baseUrl,
  timeoutMs: API.timeoutMs,
  // El navegador no maneja el JWT: viaja en la cookie HttpOnly.
  getToken: () => null,
  onLog: requestLog.push,
  // La sesión (session.js) escucha este evento para cerrarla.
  onUnauthorized: () => window.dispatchEvent(new Event('huella:unauthorized')),
});

export const api = createApi(request);
