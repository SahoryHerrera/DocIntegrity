import { useEffect, useState, useSyncExternalStore } from 'react';
import { api } from '../lib/api.js';
import { documentsStore } from '../lib/documentsStore.js';

/* Listado compartido. Al montarse pide los datos si faltan o están viejos. */
export function useDocuments() {
  const state = useSyncExternalStore(documentsStore.subscribe, documentsStore.getSnapshot);
  useEffect(() => { documentsStore.ensure(); }, []);
  return { ...state, reload: documentsStore.load };
}

/* Pide un recurso por id con cancelación y reintento. */
function useResource(fetcher, id) {
  const [state, setState] = useState({ status: 'loading', data: null, error: null });
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const ctrl = new AbortController();
    fetcher(id, { signal: ctrl.signal })
      .then((data) => setState({ status: 'ready', data, error: null }))
      .catch((error) => { if (error.kind !== 'aborted') setState({ status: 'error', data: null, error }); });
    return () => ctrl.abort();
  }, [fetcher, id, attempt]);
  const retry = () => { setState({ status: 'loading', data: null, error: null }); setAttempt((n) => n + 1); };
  return { ...state, retry };
}

/* Detalle de un documento, siempre pedido a GET /api/documents/{id}. */
export function useDocument(id) {
  const { data, ...rest } = useResource(api.documents.get, id);
  return { ...rest, doc: data };
}

/* Estado de la huella en la blockchain activa, consultado en el momento. */
export function useBlockchainStatus(id) {
  const { data, ...rest } = useResource(api.documents.blockchainStatus, id);
  return { ...rest, chain: data };
}
