/*
 * Sesión del navegador: guarda el JWT que emite POST /api/auth/google.
 * Usa las mismas claves que el inicio de sesión original (App.jsx).
 *
 * El navegador no puede validar la firma del JWT (el secreto vive solo en el
 * backend); aquí solo se lee `exp` para no enviar un token ya vencido.
 * Quien decide si el token es válido es siempre el servidor.
 */

const KEYS = { token: 'docintegrity_token', user: 'docintegrity_user', expiresAt: 'docintegrity_expires_at' };

const storage = () => { try { return typeof localStorage === 'undefined' ? null : localStorage; } catch { return null; } };

export function decodeJwtPayload(token) {
  try {
    const part = String(token).split('.')[1];
    const b64 = part.replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64 + '==='.slice((b64.length + 3) % 4));
    return JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0))));
  } catch {
    return null;
  }
}

function expiryOf(token, stored) {
  const fromStore = stored ? Date.parse(stored) : NaN;
  if (!Number.isNaN(fromStore)) return fromStore;
  const payload = decodeJwtPayload(token);
  return payload && payload.exp ? payload.exp * 1000 : NaN;
}

/* { token, user, expiresAt (ms) } o null si no hay sesión o ya venció. */
export function readSession(now = Date.now()) {
  const s = storage();
  if (!s) return null;
  const token = s.getItem(KEYS.token);
  if (!token) return null;
  const expiresAt = expiryOf(token, s.getItem(KEYS.expiresAt));
  if (Number.isNaN(expiresAt) || expiresAt <= now) return null;
  let user;
  try { user = JSON.parse(s.getItem(KEYS.user) || 'null'); } catch { user = null; }
  return { token, user, expiresAt };
}

export const getValidToken = () => (readSession() || {}).token || null;

/* Recibe el AuthResponseDto del backend: { token, expiresAt, user }. */
export function saveSession({ token, expiresAt, user }) {
  const s = storage();
  if (!s) return;
  s.setItem(KEYS.token, token);
  s.setItem(KEYS.user, JSON.stringify(user || null));
  if (expiresAt) s.setItem(KEYS.expiresAt, expiresAt); else s.removeItem(KEYS.expiresAt);
}

export function clearSession() {
  const s = storage();
  if (!s) return;
  Object.values(KEYS).forEach((k) => s.removeItem(k));
}
