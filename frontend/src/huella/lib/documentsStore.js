/*
 * Estado compartido del listado de documentos (GET /api/documents).
 * Vive en memoria: nunca se guarda en el navegador.
 */
import { api } from './api.js';

const STALE_MS = 30_000;
let state = { status: 'idle', items: [], error: null, loadedAt: 0 };
let inflight = null;
const listeners = new Set();
const emit = (patch) => { state = { ...state, ...patch }; listeners.forEach((fn) => fn()); };

export const documentsStore = {
  getSnapshot: () => state,
  subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },

  /* Vuelve a pedir la lista. Si ya hay una petición en curso, la reutiliza. */
  load() {
    if (inflight) return inflight;
    emit({ status: state.items.length ? 'refreshing' : 'loading', error: null });
    inflight = api.documents.list()
      .then((items) => emit({ status: 'ready', items, error: null, loadedAt: Date.now() }))
      .catch((error) => emit({ status: 'error', error }))
      .finally(() => { inflight = null; });
    return inflight;
  },

  /* Carga si nunca se cargó o si los datos tienen más de 30 s. */
  ensure() {
    if (state.status === 'idle' || (state.status === 'ready' && Date.now() - state.loadedAt > STALE_MS)) return documentsStore.load();
    return inflight;
  },
};
