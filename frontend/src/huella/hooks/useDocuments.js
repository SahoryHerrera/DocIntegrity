import { useEffect, useState, useSyncExternalStore } from 'react';
import { api } from '../lib/api.js';
import { documentsStore } from '../lib/documentsStore.js';

/* Listado compartido. Al montarse pide los datos si faltan o están viejos. */
export function useDocuments() {
  const state = useSyncExternalStore(documentsStore.subscribe, documentsStore.getSnapshot);
  useEffect(() => { documentsStore.ensure(); }, []);
  return { ...state, reload: documentsStore.load };
}

/* Detalle de un documento, siempre pedido a GET /api/documents/{id}. */
export function useDocument(id) {
  const [state, setState] = useState({ status: 'loading', doc: null, error: null });
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const ctrl = new AbortController();
    api.documents.get(id, { signal: ctrl.signal })
      .then((doc) => setState({ status: 'ready', doc, error: null }))
      .catch((error) => { if (error.kind !== 'aborted') setState({ status: 'error', doc: null, error }); });
    return () => ctrl.abort();
  }, [id, attempt]);
  const retry = () => { setState({ status: 'loading', doc: null, error: null }); setAttempt((n) => n + 1); };
  return { ...state, retry };
}
