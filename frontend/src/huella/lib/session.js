/*
 * Sesión del navegador. El JWT vive SOLO en una cookie HttpOnly que fija el
 * backend: JavaScript no puede leerlo, así que aquí no se guarda ningún token.
 * Este módulo conoce únicamente al usuario y la hora de vencimiento, que
 * obtiene de GET /api/auth/me.
 */
import { api } from './api.js';
import { documentsStore } from './documentsStore.js';
import { registerStore } from './registerStore.js';

// Claves del inicio de sesión anterior (App.jsx), que guardaba el JWT en localStorage.
const LEGACY_KEYS = ['docintegrity_token', 'docintegrity_user', 'docintegrity_expires_at'];
try { LEGACY_KEYS.forEach((k) => localStorage.removeItem(k)); } catch { /* sin almacenamiento */ }

const initial = () => ({
  status: 'checking', // checking | anonymous | authenticated | unavailable
  user: null,
  expiresAt: 0,
  sessionMinutes: 60,
  pending: false, // validando el inicio de sesión con el servidor
  error: null, // error del último intento
  notice: null, // { tone, text } para la pantalla de inicio
});

let state = initial();
let expiryTimer = null;
const listeners = new Set();
const emit = (patch) => { state = { ...state, ...patch }; listeners.forEach((fn) => fn()); };

/* Datos del usuario anterior fuera de la memoria de la aplicación. */
function clearUserData() {
  documentsStore.reset();
  registerStore.clear();
}

function schedule(expiresAt) {
  clearTimeout(expiryTimer);
  const ms = expiresAt - Date.now();
  // setTimeout admite como máximo unos 24 días.
  if (ms > 0 && ms < 2 ** 31) expiryTimer = setTimeout(() => authStore.expire(), ms);
}

function setAuthenticated(session) {
  if (state.user && state.user.id !== session.user.id) clearUserData();
  schedule(session.expiresAt);
  emit({ status: 'authenticated', user: session.user, expiresAt: session.expiresAt, sessionMinutes: session.sessionMinutes, pending: false, error: null, notice: null });
}

function setAnonymous(notice = null) {
  clearTimeout(expiryTimer);
  clearUserData();
  emit({ ...initial(), status: 'anonymous', notice });
}

export const authStore = {
  getSnapshot: () => state,
  subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },

  /* Al cargar la página: la cookie, si existe, la envía el navegador. */
  async restore() {
    emit({ status: 'checking', error: null });
    try {
      setAuthenticated(await api.auth.me());
    } catch (error) {
      if (error.status === 401) setAnonymous();
      else emit({ status: 'unavailable', error });
    }
  },

  /* credential: el ID token que entrega el botón de Google. */
  async loginWithGoogle(credential) {
    emit({ pending: true, error: null, notice: null });
    try {
      await api.auth.google(credential);
      setAuthenticated(await api.auth.me());
    } catch (error) {
      emit({ pending: false, error });
    }
  },

  googleFailed() {
    emit({ pending: false, error: { message: 'Google no pudo completar el inicio de sesión. Inténtalo de nuevo.' } });
  },

  async logout() {
    try { await api.auth.logout(); } catch { /* la sesión local se cierra igualmente */ }
    setAnonymous({ tone: 'seal', text: 'Cerraste tu sesión.' });
  },

  /* Vencimiento o cualquier 401 de la API con la sesión abierta. */
  expire() {
    if (state.status !== 'authenticated') return;
    api.auth.logout().catch(() => {});
    setAnonymous({ tone: 'warn', text: 'Tu sesión expiró. Inicia sesión de nuevo para continuar.' });
  },
};

window.addEventListener('huella:unauthorized', () => authStore.expire());
