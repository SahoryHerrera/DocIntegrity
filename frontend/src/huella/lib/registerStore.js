/*
 * Estado del registro en curso. Vive fuera de la vista para que el resultado
 * no se pierda si el usuario navega mientras el servidor procesa (con IA local
 * puede tardar minutos). Solo en memoria.
 */
import { api } from './api.js';
import { documentsStore } from './documentsStore.js';
import { validatePdf } from './files.js';
import { sha256Hex } from './sha256.js';

const fresh = () => ({
  status: 'idle', // idle | ready | running | done | duplicate | error
  file: null, // File elegido
  previewSha: null, // calculado en el navegador, solo como vista previa
  result: null, // documento normalizado devuelto por el backend
  conflict: null, // cuerpo del 409
  error: null, // ApiError o mensaje de validación
  startedAt: 0,
  finishedAt: 0,
  seen: false, // la animación del resultado ya se mostró
});

let state = fresh();
// Cambia al cerrar sesión: el resultado de un registro de la sesión anterior se descarta.
let generation = 0;
const listeners = new Set();
const emit = (patch) => { state = { ...state, ...patch }; listeners.forEach((fn) => fn()); };

export const registerStore = {
  getSnapshot: () => state,
  subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },

  reset() { if (state.status !== 'running') { state = fresh(); listeners.forEach((fn) => fn()); } },

  /* Cierre de sesión: borra todo, incluido un registro en curso cuyo resultado ya no se mostrará. */
  clear() { generation++; state = fresh(); listeners.forEach((fn) => fn()); },

  /* Al volver a la vista, un resultado ya visto se descarta. */
  leave() { if (['done', 'duplicate', 'error'].includes(state.status) && state.seen) registerStore.reset(); },
  markSeen() { if (!state.seen && ['done', 'duplicate', 'error'].includes(state.status)) state = { ...state, seen: true }; },

  async choose(file) {
    if (state.status === 'running') return;
    const v = await validatePdf(file);
    if (v.error) { state = fresh(); emit({ error: { message: v.error, kind: 'validation' } }); return; }
    const previewSha = await sha256Hex(v.bytes);
    state = fresh();
    emit({ status: 'ready', file, previewSha });
  },

  async start() {
    if (state.status !== 'ready') return;
    const gen = generation;
    emit({ status: 'running', error: null, conflict: null, result: null, startedAt: Date.now(), finishedAt: 0, seen: false });
    try {
      const result = await api.documents.register(state.file);
      if (gen !== generation) return;
      emit({ status: 'done', result, finishedAt: Date.now() });
      documentsStore.load(); // el nuevo registro aparece en Documentos y el Panel
    } catch (error) {
      if (gen !== generation) return;
      if (error.status === 409) {
        emit({ status: 'duplicate', conflict: error.data || { message: error.message }, finishedAt: Date.now() });
      } else {
        emit({ status: 'error', error, finishedAt: Date.now() });
        // Tras un tiempo de espera el servidor pudo terminar el registro igualmente.
        if (error.kind === 'timeout') documentsStore.load();
      }
    }
  },
};
