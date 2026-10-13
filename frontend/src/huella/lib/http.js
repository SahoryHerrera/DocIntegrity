/*
 * Cliente HTTP centralizado sobre fetch: JSON, FormData, Bearer, tiempos de
 * espera y errores tipados. No depende de React ni del navegador más allá de
 * fetch, AbortController y FormData.
 */

export class ApiError extends Error {
  /**
   * kind: 'http' | 'network' | 'timeout' | 'aborted' | 'parse' | 'config'
   * status: código HTTP (0 si no hubo respuesta)
   * data: cuerpo JSON del error, si el servidor lo envió
   */
  constructor(kind, message, { status = 0, data = null, method = '', path = '' } = {}) {
    super(message);
    this.name = 'ApiError';
    Object.assign(this, { kind, status, data, method, path });
  }
}

const STATUS_TEXT = {
  400: 'La solicitud no es válida.',
  401: 'Tu sesión no es válida o expiró. Inicia sesión de nuevo.',
  403: 'No tienes permiso para realizar esta acción.',
  404: 'El recurso solicitado no existe.',
  409: 'El recurso ya existe.',
  413: 'El archivo es demasiado grande para el servidor.',
  415: 'El formato enviado no es compatible.',
  429: 'Demasiadas solicitudes. Espera un momento e intenta de nuevo.',
  500: 'El servidor encontró un error al procesar la solicitud.',
  502: 'Un servicio necesario no respondió correctamente. Intenta de nuevo más tarde.',
  503: 'Un servicio necesario no está disponible. Intenta de nuevo más tarde.',
  504: 'Un servicio necesario tardó demasiado en responder.',
};

/*
 * Mensaje para el usuario. Se usa `message` cuando la API lo envía (así
 * responde DocumentsController). Nunca se muestra el cuerpo crudo de un error
 * que no sea JSON, como la página de excepción de desarrollo.
 */
function messageFor(status, data) {
  if (data && typeof data.message === 'string' && data.message.trim()) return data.message.trim();
  if (status === 403) return STATUS_TEXT[403];
  return STATUS_TEXT[status] || (status >= 500 ? STATUS_TEXT[500] : STATUS_TEXT[400]);
}

async function readBody(res) {
  const text = await res.text();
  if (!text) return { json: null, isJson: true };
  const type = res.headers.get('content-type') || '';
  if (!/json/i.test(type)) return { json: null, isJson: false };
  try { return { json: JSON.parse(text), isJson: true }; } catch { return { json: null, isJson: false }; }
}

/**
 * Crea una función request(method, path, options).
 * options: { json, form, auth = true, timeoutMs, signal }
 */
export function createHttpClient({ baseUrl, getToken = () => null, timeoutMs = 30_000, onLog = () => {}, onUnauthorized = () => {} }) {
  return async function request(method, path, { json, form, auth = true, timeoutMs: ownTimeout, signal } = {}) {
    const meta = { method, path };
    if (!baseUrl) throw new ApiError('config', 'Falta configurar la URL de la API (VITE_API_URL).', meta);

    const headers = { Accept: 'application/json' };
    let body;
    if (form) body = form; // el navegador pone el multipart/form-data con su boundary
    else if (json !== undefined) { body = JSON.stringify(json); headers['Content-Type'] = 'application/json'; }
    // Protección CSRF: el backend exige esta cabecera en las solicitudes que modifican datos.
    if (method !== 'GET' && method !== 'HEAD') headers['X-Requested-With'] = 'Huella';
    // Clientes de API: Bearer opcional. El navegador usa la cookie HttpOnly.
    const token = auth ? getToken() : null;
    if (token) headers.Authorization = `Bearer ${token}`;

    const limit = ownTimeout ?? timeoutMs;
    const ctrl = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; ctrl.abort(); }, limit);
    const onAbort = () => ctrl.abort();
    if (signal) { if (signal.aborted) ctrl.abort(); else signal.addEventListener('abort', onAbort, { once: true }); }

    const t0 = performance.now();
    let status = 0;
    try {
      let res; let parsed;
      try {
        // credentials: 'include' envía la cookie de sesión (HttpOnly) a la API.
        res = await fetch(`${baseUrl}${path}`, { method, headers, body, signal: ctrl.signal, credentials: 'include' });
        status = res.status;
        parsed = await readBody(res);
      } catch {
        if (timedOut) throw new ApiError('timeout', `El servidor no respondió en ${Math.max(1, Math.round(limit / 1000))} segundos.`, { ...meta, status });
        if (ctrl.signal.aborted) throw new ApiError('aborted', 'La solicitud se canceló.', { ...meta, status });
        throw new ApiError('network', 'No se pudo conectar con la API de DocIntegrity. Comprueba que el backend esté en ejecución.', meta);
      }
      if (!res.ok) {
        if (status === 401) onUnauthorized();
        throw new ApiError('http', messageFor(status, parsed.json), { ...meta, status, data: parsed.json });
      }
      if (!parsed.isJson) throw new ApiError('parse', 'La API devolvió una respuesta con un formato inesperado.', { ...meta, status });
      return parsed.json;
    } finally {
      clearTimeout(timer);
      if (signal) signal.removeEventListener('abort', onAbort);
      onLog({ method, path, status, ms: Math.round(performance.now() - t0), at: new Date().toISOString() });
    }
  };
}
