import { useSyncExternalStore } from 'react';
import { requestLog } from '../lib/requestLog.js';

/* Últimas solicitudes reales a la API, en memoria. */
export const useRequestLog = () => useSyncExternalStore(requestLog.subscribe, requestLog.getSnapshot);
