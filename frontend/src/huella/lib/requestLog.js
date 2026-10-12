/*
 * Registro en memoria de las últimas solicitudes reales a la API (para Ajustes).
 * No se guarda en el navegador y se pierde al recargar.
 */

const MAX = 40;
let entries = [];
const listeners = new Set();

export const requestLog = {
  push(entry) {
    entries = [entry, ...entries].slice(0, MAX);
    listeners.forEach((fn) => fn());
  },
  getSnapshot: () => entries,
  subscribe(fn) {
    listeners.add(fn);
    return () => listeners.delete(fn);
  },
};
